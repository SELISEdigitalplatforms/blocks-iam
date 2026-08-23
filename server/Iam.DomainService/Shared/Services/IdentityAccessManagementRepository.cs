using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Iam.DomainService.Services
{
    public class IdentityAccessManagementRepository : BaseRepository, IIdentityAccessManagementRepository
    {
        private const string IdentityConfigurationCollectionName = "IdentityConfigurations";
        private const string BlackListCollectionName = "BlackListInformations";
        private const string BlackListIndexName = "ix_key_value";
        private const string BlackListPasswordKey = "password";

        private readonly IBlocksSecret _blocksSecret;
        private readonly ILogger<IdentityAccessManagementRepository> _logger;

        public IdentityAccessManagementRepository(
            IDbContextProvider dbContextProvider,
            IBlocksSecret blocksSecret,
            ILogger<IdentityAccessManagementRepository> logger) : base(dbContextProvider)
        {
            _blocksSecret = blocksSecret;
            _logger = logger;
        }

        /// <summary>
        /// The password blacklist is global on purpose: it lives in the root database so one entry
        /// blocks that password for every tenant. Resolving it per tenant, as this used to, meant a
        /// blacklisted password was enforced for exactly one tenant and silently ignored everywhere
        /// else.
        /// </summary>
        private IMongoCollection<BlackListInformation> GetBlackListCollection() =>
            _dbContextProvider
                .GetDatabase(_blocksSecret.DatabaseConnectionString, _blocksSecret.RootDatabaseName, false)
                .GetCollection<BlackListInformation>(BlackListCollectionName);

        public async Task<IamConfiguration> GetIamConfigurationAsync()
        {
            var collection = GetCollectionByName<IamConfiguration>(IdentityConfigurationCollectionName);

            return await collection.Find(_ => true).FirstOrDefaultAsync();
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            var collection = GetCollection<User>();
            var options = new FindOptions<User>
            {
                Collation = new Collation("en", strength: CollationStrength.Secondary)
            };
            var filter = Builders<User>.Filter.Eq(x => x.Email, NormalizeEmail(email));

            return await (await collection.FindAsync(filter, options)).FirstOrDefaultAsync();
        }

        public async Task<User> GetUserByIdAsync(string itemId)
        {
            var collection = GetCollection<User>();

            return await collection.Find(x => x.ItemId == itemId).FirstOrDefaultAsync();
        }

        public async Task<T> GetUserByIdAsync<T>(string itemId)
        {
            var collection = GetCollection<User>();
            var filter = Builders<User>.Filter.Eq(x => x.ItemId, itemId);
            var project = Builders<User>.Projection.As<T>();

            var cursor = await collection.FindAsync(filter, new FindOptions<User, T>
            {
                Projection = project
            });
            return await cursor.FirstOrDefaultAsync();
        }

        public async Task<bool> CheckPasswordBlackListedAsync(string password)
        {
            // Deliberately unguarded: no tenant check, and no try/catch. If the root database
            // cannot be reached the exception must reach the caller and fail the request, because
            // reporting "not blacklisted" on an outage is precisely the failure this replaces.
            var collection = GetBlackListCollection();
            var result = await collection.CountDocumentsAsync(x => x.Key == BlackListPasswordKey && x.Value == password);

            return result > 0;
        }

        public async Task EnsureIndexesAsync(CancellationToken ct = default)
        {
            try
            {
                var keys = Builders<BlackListInformation>.IndexKeys;
                await GetBlackListCollection().Indexes.CreateOneAsync(
                    new CreateIndexModel<BlackListInformation>(
                        keys.Ascending(x => x.Key).Ascending(x => x.Value),
                        new CreateIndexOptions<BlackListInformation> { Name = BlackListIndexName }),
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                // Startup must not hinge on index creation: the lookup is correct without the
                // index, only slower, and creation is idempotent so a later start retries it.
                _logger.LogWarning(ex, "IdentityAccessManagementRepository.EnsureIndexesAsync failed; index creation is idempotent and will be retried.");
            }
        }

        public async Task<bool> InsertUserKeyMapAsync(UserKeyMap userKeyMap)
        {
            var collection = GetCollection<UserKeyMap>();
            await collection.InsertOneAsync(userKeyMap);

            return true;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            NormalizeUserIdentity(user);
            var collection = GetCollection<User>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == user.ItemId, user);

            return result.IsAcknowledged;
        }


        public async Task<bool> UpdateUserKeyMapActivationAsync(string userId)
        {
            var collection = GetCollection<UserKeyMap>();
            var update = Builders<UserKeyMap>.Update.Set(u => u.Activated, true);
            var result = await collection.UpdateOneAsync(u => u.UserId == userId && !u.Activated, update);

            return result.IsAcknowledged;
        }

        public async Task<List<UserKeyMap>> GetActiveUserKeyMapAsync(string userId)
        {
            var collection = GetCollection<UserKeyMap>();
            return await collection.Find(u => u.UserId == userId && !u.Activated).ToListAsync();
        }

        public async Task<string> GetUserIdFromKeyMapByKeyAsync(string key)
        {
            var collection = GetCollection<UserKeyMap>();

            // Build the filter
            var filter = Builders<UserKeyMap>.Filter.Eq(u => u.Key, key);

            // Build the projection (map to string UserId only)
            var projection = Builders<UserKeyMap>.Projection.Expression(u => u.UserId);

            // Execute FindAsync with filter + projection
            using var cursor = await collection.FindAsync(filter, new FindOptions<UserKeyMap, string>
            {
                Projection = projection
            });

            var userId = await cursor.FirstOrDefaultAsync();

            // No key-map row for this key: nothing to activate. Return empty rather than looking up a null user.
            if (string.IsNullOrWhiteSpace(userId)) return string.Empty;

            var user = await GetUserByIdAsync(userId);

            // The key maps to a user that no longer exists (deleted, or a partial write during minting).
            // Guard against dereferencing null — this previously threw a NullReferenceException (HTTP 500),
            // which the activation page rendered as a misleading "Invalid Activation Link".
            if (user is null) return string.Empty;

            // Only surface the user while the account still needs activation; an already-active account
            // has nothing to activate.
            return user.Active ? string.Empty : user.ItemId;
        }

        public async Task SaveSignUpSettingAsync(TenantConfiguration tenantConfiguration)
        {
            var collection = GetCollection<TenantConfiguration>();

            var result = await collection.UpdateOneAsync(
                Builders<TenantConfiguration>.Filter.Empty,
                Builders<TenantConfiguration>.Update
                    .Set(t => t.IsEmailPasswordSignUpEnabled, tenantConfiguration.IsEmailPasswordSignUpEnabled)
                    .Set(t => t.IsSSoSignUpEnabled, tenantConfiguration.IsSSoSignUpEnabled)
                    .Set(t => t.DefaultRolesForNewUserOnSignUp, tenantConfiguration.DefaultRolesForNewUserOnSignUp)
                    .Set(t => t.DefaultPermissionsForNewUserOnSignUp, tenantConfiguration.DefaultPermissionsForNewUserOnSignUp)
                    .Set(t => t.LastUpdatedBy, tenantConfiguration.LastUpdatedBy)
                    .Set(t => t.LastUpdatedDate, tenantConfiguration.LastUpdatedDate));
        }

        public async Task<TenantConfiguration> GetTenantConfigurationAsync()
        {
            var collection = GetCollection<TenantConfiguration>();
            var tenantConfiguration = await collection.Find(_ => true).FirstOrDefaultAsync();
            return tenantConfiguration;
        }

        private static void NormalizeUserIdentity(User user)
        {
            user.Email = NormalizeEmail(user.Email);
            user.UserName = NormalizeIdentity(user.UserName);
        }

        private static string NormalizeEmail(string? email)
        {
            return string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
        }

        private static string NormalizeIdentity(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }
    }
}
