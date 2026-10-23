using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

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
        private readonly TenantConfigurationStore _tenantConfigurationStore;

        public IdentityAccessManagementRepository(
            IDbContextProvider dbContextProvider,
            IBlocksSecret blocksSecret,
            ILogger<IdentityAccessManagementRepository> logger,
            IIamDataAccess dataAccess) : base(dbContextProvider, dataAccess)
        {
            _blocksSecret = blocksSecret;
            _logger = logger;
            _tenantConfigurationStore = new TenantConfigurationStore(dbContextProvider, dataAccess);
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

            return await _dataAccess.FindFirstAsync(collection, IamSchemas.IdentityConfiguration, Builders<IamConfiguration>.Filter.Empty, "IdentityAccessManagementRepository.GetIamConfigurationAsync");
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            var collection = GetCollection<User>();
            var options = new FindOptions<User>
            {
                Collation = new Collation("en", strength: CollationStrength.Secondary)
            };
            var filter = Builders<User>.Filter.Eq(x => x.Email, NormalizeEmail(email));

            var scope = await _dataAccess.ReadAsync(IamSchemas.User, filter, "IdentityAccessManagementRepository.GetUserByEmailAsync", useProjection: false);
            if (!scope.Granted) return null!;
            return await (await collection.FindAsync(scope.Filter, options)).FirstOrDefaultAsync();
        }

        public async Task<User> GetUserByIdAsync(string itemId)
        {
            var collection = GetCollection<User>();

            return await _dataAccess.FindFirstAsync(collection, IamSchemas.User, Builders<User>.Filter.Eq(x => x.ItemId, itemId), "IdentityAccessManagementRepository.GetUserByIdAsync") ?? null!;
        }

        public async Task<T> GetUserByIdAsync<T>(string itemId)
        {
            var collection = GetCollection<User>();
            var filter = Builders<User>.Filter.Eq(x => x.ItemId, itemId);
            var project = Builders<User>.Projection.As<T>();

            // Own projection (As<T>): only the row filter is applied, not the resolver projection.
            var scope = await _dataAccess.ReadAsync(IamSchemas.User, filter, "IdentityAccessManagementRepository.GetUserByIdAsync<T>", useProjection: false);
            if (!scope.Granted) return default!;
            var cursor = await collection.FindAsync(scope.Filter, new FindOptions<User, T>
            {
                Projection = project
            });
            // Only GET iam/me and iam/users/{id} read this: View column (property) rules clear what the caller may not see.
            return scope.ForViewAs(await cursor.FirstOrDefaultAsync());
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
            await _dataAccess.InsertOneAsync(collection, IamSchemas.UserKeyMap, userKeyMap, "IdentityAccessManagementRepository.InsertUserKeyMapAsync");

            return true;
        }

        public async Task<bool> UpdateUserAsync(User user)
        {
            NormalizeUserIdentity(user);
            var collection = GetCollection<User>();
            var result = await _dataAccess.ReplaceOneAsync(collection, IamSchemas.User, Builders<User>.Filter.Eq(x => x.ItemId, user.ItemId), user, "IdentityAccessManagementRepository.UpdateUserAsync");

            return result?.IsAcknowledged ?? false;
        }

        public async Task<bool> RecordSuccessfulLoginAsync(string userId, string deviceInformationJson, DateTime nowUtc)
        {
            var collection = GetCollection<User>();
            var filter = Builders<User>.Filter.Eq(x => x.ItemId, userId);

            // $inc, not a read-then-write: the increment is evaluated by the server against whatever
            // the document holds at that instant, so two logins landing together produce two
            // increments. The same update carries the other two fields the old replace wrote, so
            // there is still exactly one round trip on the common path.
            var update = Builders<User>.Update
                .Inc(x => x.LogInCount, 1)
                .Set(x => x.LastLoggedInTime, nowUtc)
                .Set(x => x.LastLoggedInDeviceInfo, deviceInformationJson);

            var updated = await _dataAccess.FindOneAndUpdateAsync(
                collection,
                IamSchemas.User,
                filter,
                update,
                "IdentityAccessManagementRepository.RecordSuccessfulLoginAsync",
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });

            if (updated == null)
            {
                return false;
            }

            // Same condition the replace applied - "LogInCount was 0 before this login" - read off the
            // post-increment document instead of a pre-read one. The extra filter on the field still
            // being unset makes the write idempotent, so two first logins racing cannot overwrite an
            // already-recorded FirstLoggedInTime with a later one.
            if (updated.LogInCount == 1)
            {
                await _dataAccess.UpdateOneAsync(
                    collection,
                    IamSchemas.User,
                    Builders<User>.Filter.And(
                        filter,
                        Builders<User>.Filter.Eq(x => x.FirstLoggedInTime, default(DateTime))),
                    Builders<User>.Update.Set(x => x.FirstLoggedInTime, nowUtc),
                    "IdentityAccessManagementRepository.RecordSuccessfulLoginAsync.FirstLogin");
            }

            return true;
        }


        public async Task<bool> UpdateUserKeyMapActivationAsync(string userId)
        {
            var collection = GetCollection<UserKeyMap>();
            var update = Builders<UserKeyMap>.Update.Set(u => u.Activated, true);
            // Every outstanding code for the user, not one of them: the cache side of this
            // invalidation drops them all, and leaving rows behind would let a resent link still
            // look unused.
            var result = await _dataAccess.UpdateManyAsync(collection, IamSchemas.UserKeyMap, Builders<UserKeyMap>.Filter.Where(u => u.UserId == userId && !u.Activated), update, "IdentityAccessManagementRepository.UpdateUserKeyMapActivationAsync");

            return result?.IsAcknowledged ?? false;
        }

        public async Task<List<UserKeyMap>> GetActiveUserKeyMapAsync(string userId)
        {
            var collection = GetCollection<UserKeyMap>();
            return await _dataAccess.FindListAsync(collection, IamSchemas.UserKeyMap, Builders<UserKeyMap>.Filter.Where(u => u.UserId == userId && !u.Activated), "IdentityAccessManagementRepository.GetActiveUserKeyMapAsync");
        }


        public async Task<UserKeyMap?> GetUserKeyMapByKeyAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var collection = GetCollection<UserKeyMap>();
            return await _dataAccess.FindFirstAsync(collection, IamSchemas.UserKeyMap, Builders<UserKeyMap>.Filter.Eq(u => u.Key, key), "IdentityAccessManagementRepository.GetUserKeyMapByKeyAsync");
        }

        public async Task<string> GetUserIdFromKeyMapByKeyAsync(string key)
        {
            var collection = GetCollection<UserKeyMap>();

            // Build the filter
            var filter = Builders<UserKeyMap>.Filter.Eq(u => u.Key, key);

            // Build the projection (map to string UserId only)
            var projection = Builders<UserKeyMap>.Projection.Expression(u => u.UserId);

            // Execute FindAsync with filter + projection
            var scope = await _dataAccess.ReadAsync(IamSchemas.UserKeyMap, filter, "IdentityAccessManagementRepository.GetUserIdFromKeyMapByKeyAsync", useProjection: false);
            if (!scope.Granted) return string.Empty;
            using var cursor = await collection.FindAsync(scope.Filter, new FindOptions<UserKeyMap, string>
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
            await _tenantConfigurationStore.UpdateAsync(
                Builders<TenantConfiguration>.Update
                    .Set(t => t.IsEmailPasswordSignUpEnabled, tenantConfiguration.IsEmailPasswordSignUpEnabled)
                    .Set(t => t.IsSSoSignUpEnabled, tenantConfiguration.IsSSoSignUpEnabled)
                    .Set(t => t.DefaultRolesForNewUserOnSignUp, tenantConfiguration.DefaultRolesForNewUserOnSignUp)
                    .Set(t => t.DefaultPermissionsForNewUserOnSignUp, tenantConfiguration.DefaultPermissionsForNewUserOnSignUp)
                    .Set(t => t.LastUpdatedBy, tenantConfiguration.LastUpdatedBy)
                    .Set(t => t.LastUpdatedDate, tenantConfiguration.LastUpdatedDate));
        }

        public async Task<IamTenantConfiguration> GetTenantConfigurationAsync()
        {
            return (await _tenantConfigurationStore.ReadAsync())!;
        }

        public Task<bool> PullSignUpDefaultRoleAsync(string slug) =>
            PullSignUpDefaultAsync(Builders<TenantConfiguration>.Update.Pull(t => t.DefaultRolesForNewUserOnSignUp, slug));

        public Task<bool> PullSignUpDefaultPermissionAsync(string resource) =>
            PullSignUpDefaultAsync(Builders<TenantConfiguration>.Update.Pull(t => t.DefaultPermissionsForNewUserOnSignUp, resource));

        private Task<bool> PullSignUpDefaultAsync(UpdateDefinition<TenantConfiguration> update) =>
            _tenantConfigurationStore.UpdateAsync(update);

        public Task SaveOrganizationConfigAsync(IamTenantConfiguration tenantConfiguration) =>
            _tenantConfigurationStore.UpdateAsync(
                Builders<IamTenantConfiguration>.Update
                    .SetOnInsert(c => c.ItemId, tenantConfiguration.ItemId)
                    .SetOnInsert(c => c.CreatedBy, tenantConfiguration.CreatedBy)
                    .SetOnInsert(c => c.CreatedDate, tenantConfiguration.CreatedDate)
                    .Set(c => c.AllowOrgCreationFromCloud, tenantConfiguration.AllowOrgCreationFromCloud)
                    .Set(c => c.AllowOrgCreationFromConstruct, tenantConfiguration.AllowOrgCreationFromConstruct)
                    .Set(c => c.AllowOrgCreationFromSignup, tenantConfiguration.AllowOrgCreationFromSignup)
                    .Set(c => c.AllowOrgCreationFromPortal, tenantConfiguration.AllowOrgCreationFromPortal)
                    .Set(c => c.IsMultiOrgEnabled, tenantConfiguration.IsMultiOrgEnabled)
                    .Set(c => c.IsOrgNameUniquenessEnabled, tenantConfiguration.IsOrgNameUniquenessEnabled)
                    .Set(c => c.IsSignupAutoJoinEnabled, tenantConfiguration.IsSignupAutoJoinEnabled)
                    .Set(c => c.SignupAutoJoinOrganizationId, tenantConfiguration.SignupAutoJoinOrganizationId)
                    .Set(c => c.ConsentForMultiOrgEnable, tenantConfiguration.ConsentForMultiOrgEnable)
                    .Set(c => c.LastUpdatedBy, tenantConfiguration.LastUpdatedBy)
                    .Set(c => c.DefaultPermissionsForNewUserOnSignUp, tenantConfiguration.DefaultPermissionsForNewUserOnSignUp)
                    .Set(c => c.LastUpdatedDate, tenantConfiguration.LastUpdatedDate),
                upsertLegacy: true);

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
