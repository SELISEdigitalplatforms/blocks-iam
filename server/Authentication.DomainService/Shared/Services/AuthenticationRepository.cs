using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Authentication.DomainService.Shared.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.Services
{
    public sealed class AuthenticationRepository : IAuthenticationRepository
    {
        private const string OidcClientRegistrationsCollectionName = "OidcClientRegistrations";
        private const string SocialLoginScopes = "openid profile email";

        /// <summary>The singleton key used for each tenant's OIDC UI template.</summary>
        public const string OidcUiTemplateStoreKey = "oidcUiTemplate";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly OidcDiscoveryClient _oidcDiscoveryClient;
        private readonly IKeyValueStore _keyValueStore;
        private readonly IIamDataAccess _dataAccess;

        public AuthenticationRepository(
            IDbContextProvider dbContextProvider,
            OidcDiscoveryClient oidcDiscoveryClient,
            IKeyValueStore keyValueStore,
            IIamDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _oidcDiscoveryClient = oidcDiscoveryClient;
            _keyValueStore = keyValueStore;
            _dataAccess = dataAccess;
        }

        public IMongoCollection<T> GetCollection<T>()
        {
            return _dbContextProvider.GetCollection<T>($"{typeof(T).Name}s");
        }

        public IMongoCollection<T> GetCollection<T>(string tenantId)
        {
            return _dbContextProvider.GetCollection<T>(tenantId, $"{typeof(T).Name}s");
        }

        public IMongoCollection<T> GetCollectionByName<T>(string collectionName, string tenantId)
        {
            return _dbContextProvider.GetCollection<T>(tenantId, collectionName);
        }

        public IMongoCollection<T> GetCollectionByName<T>(string collectionName)
        {
            return _dbContextProvider.GetCollection<T>(collectionName);
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            var collection = GetCollection<User>();
            email = NormalizeIdentity(email);
            // A blank identity would otherwise filter on Email == "" and match unrelated
            // documents that legitimately store an empty email (e.g. externally provisioned
            // or seed accounts). Return no user instead of a spurious match.
            if (string.IsNullOrEmpty(email))
            {
                return null!;
            }
            var options = new FindOptions<User>
            {
                Collation = new Collation("en", strength: CollationStrength.Secondary)
            };
            var filter = Builders<User>.Filter.Eq(x => x.Email, email);
            var scope = await _dataAccess.ReadAsync(IamSchemas.User, filter, "AuthenticationRepository.GetUserByEmailAsync", useProjection: false);
            if (!scope.Granted) return null!;
            return await (await collection.FindAsync(scope.Filter, options)).FirstOrDefaultAsync();
        }

        public async Task<User> GetUserByUsernameAsync(string username, string? organizationId = null)
        {
            var collection = GetCollection<User>();
            username = NormalizeIdentity(username);
            // A blank identity would otherwise filter on UserName == "" OR Email == "" and match
            // unrelated documents that legitimately store an empty value. Return no user instead
            // of a spurious match.
            if (string.IsNullOrEmpty(username))
            {
                return null!;
            }

            var filter = Builders<User>.Filter.Eq(u => u.UserName, username) | Builders<User>.Filter.Eq(u => u.Email, username);
            var options = new FindOptions
            {
                Collation = new Collation("en", strength: CollationStrength.Secondary)
            };

            if (!string.IsNullOrWhiteSpace(organizationId) && organizationId != "default")
            {

                filter &= Builders<User>.Filter.AnyEq("OrganizationIds", organizationId);
            }

            var scope = await _dataAccess.ReadAsync(IamSchemas.User, filter, "AuthenticationRepository.GetUserByUsernameAsync", useProjection: false);
            if (!scope.Granted) return null!;
            return await collection.Find(scope.Filter, options).FirstOrDefaultAsync();
        }

        public async Task<User> GetUserByIdAsync(string itemId)
        {
            var collection = GetCollection<User>();

            return (await _dataAccess.FindFirstAsync(collection, IamSchemas.User, Builders<User>.Filter.Eq(x => x.ItemId, itemId), "AuthenticationRepository.GetUserByIdAsync"))!;
        }

        public async Task<T> GetUserByIdAsync<T>(string itemId)
        {
            var collection = GetCollection<User>();
            var filter = Builders<User>.Filter.Eq(x => x.ItemId, itemId);
            var project = Builders<User>.Projection.As<T>();

            // Own projection (As<T>): only the row filter is applied, not the resolver projection.
            var scope = await _dataAccess.ReadAsync(IamSchemas.User, filter, "AuthenticationRepository.GetUserByIdAsync<T>", useProjection: false);
            if (!scope.Granted) return default!;
            var cursor = await collection.FindAsync(scope.Filter, new FindOptions<User, T>
            {
                Projection = project
            });
            return await cursor.FirstOrDefaultAsync();
        }

        /// <summary>
        /// How long the last free slot stays taken by one reservation once the threshold is reached. Shorter than
        /// any lock (minimum 1 minute) and longer than any request, so a crashed request frees the slot again.
        /// </summary>
        public static readonly TimeSpan LoginAttemptLease = TimeSpan.FromSeconds(60);

        public Task<User?> TryReserveLoginAttemptAsync(string userId, int lockThreshold, DateTime nowUtc) =>
            TryReserveAttemptAsync(LoginCounter, userId, lockThreshold, nowUtc);

        public Task<bool> ReleaseLoginAttemptAsync(string userId, DateTime nowUtc) =>
            ReleaseAttemptAsync(LoginCounter, userId, nowUtc);

        public Task<User?> LockAccountIfLoginThresholdReachedAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc) =>
            LockIfThresholdReachedAsync(LoginCounter, userId, lockThreshold, lockDurationInMinutes, nowUtc);

        public Task<User?> TryReserveMfaAttemptAsync(string userId, int lockThreshold, DateTime nowUtc) =>
            TryReserveAttemptAsync(MfaCounter, userId, lockThreshold, nowUtc);

        public Task<bool> ReleaseMfaAttemptAsync(string userId, DateTime nowUtc) =>
            ReleaseAttemptAsync(MfaCounter, userId, nowUtc);

        public Task<User?> LockAccountIfMfaThresholdReachedAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc) =>
            LockIfThresholdReachedAsync(MfaCounter, userId, lockThreshold, lockDurationInMinutes, nowUtc);

        /// <summary>One failure counter on the user: wrong passwords or wrong MFA codes.</summary>
        private sealed record AttemptCounter(
            string Name,
            Expression<Func<User, int>> Count,
            Expression<Func<User, DateTime?>> LastFailure,
            Func<User, int> Read);

        private static readonly AttemptCounter LoginCounter =
            new("Login", x => x.FailedLoginCount, x => x.LastFailedLoginUtc, x => x.FailedLoginCount);

        private static readonly AttemptCounter MfaCounter =
            new("Mfa", x => x.FailedMfaCount, x => x.LastFailedMfaUtc, x => x.FailedMfaCount);

        private async Task<User?> TryReserveAttemptAsync(AttemptCounter counter, string userId, int lockThreshold, DateTime nowUtc)
        {
            var f = Builders<User>.Filter;
            var filter = f.And(
                f.Eq(x => x.ItemId, userId),
                NotLockedFilter(nowUtc),
                f.Or(
                    f.Lt(counter.Count, lockThreshold),
                    f.Exists(new ExpressionFieldDefinition<User, int>(counter.Count), false),
                    f.Eq(counter.LastFailure, null),
                    f.Lte(counter.LastFailure, nowUtc - LoginAttemptLease)));

            var update = Builders<User>.Update
                .Inc(counter.Count, 1)
                .Set(counter.LastFailure, nowUtc)
                .Set(x => x.LastUpdatedDate, nowUtc)
                .Set(x => x.LastUpdatedBy, userId);

            return await _dataAccess.FindOneAndUpdateAsync(
                GetCollection<User>(),
                IamSchemas.User,
                filter,
                update,
                $"AuthenticationRepository.TryReserve{counter.Name}AttemptAsync",
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });
        }

        private async Task<bool> ReleaseAttemptAsync(AttemptCounter counter, string userId, DateTime nowUtc)
        {
            var collection = GetCollection<User>();
            var f = Builders<User>.Filter;
            var released = await _dataAccess.FindOneAndUpdateAsync(
                collection,
                IamSchemas.User,
                f.And(f.Eq(x => x.ItemId, userId), NotLockedFilter(nowUtc), f.Gt(counter.Count, 0)),
                Builders<User>.Update
                    .Inc(counter.Count, -1)
                    .Set(x => x.LastUpdatedDate, nowUtc)
                    .Set(x => x.LastUpdatedBy, userId),
                $"AuthenticationRepository.Release{counter.Name}AttemptAsync");
            if (released != null)
            {
                return true;
            }

            // Nothing to give back (a concurrent sign-in already reset the counter) or the account is locked.
            var current = await _dataAccess.FindFirstAsync(collection, IamSchemas.User, Builders<User>.Filter.Eq(x => x.ItemId, userId), $"AuthenticationRepository.Release{counter.Name}AttemptAsync.Read");
            return current != null && !(current.LockoutUntilUtc.HasValue && current.LockoutUntilUtc.Value > nowUtc);
        }

        private async Task<User?> LockIfThresholdReachedAsync(AttemptCounter counter, string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc)
        {
            var collection = GetCollection<User>();
            var current = await _dataAccess.FindFirstAsync(collection, IamSchemas.User, Builders<User>.Filter.Eq(x => x.ItemId, userId), $"AuthenticationRepository.LockAccountIf{counter.Name}ThresholdReachedAsync.Read");
            if (current == null
                || counter.Read(current) < lockThreshold
                || (current.LockoutUntilUtc.HasValue && current.LockoutUntilUtc.Value > nowUtc))
            {
                return null;
            }

            // LockoutCount must still be the value the duration was computed from: no double lock, exact escalation.
            var f = Builders<User>.Filter;
            var sameLockoutCount = current.LockoutCount == 0
                ? f.Or(f.Eq(x => x.LockoutCount, 0), f.Exists(x => x.LockoutCount, false))
                : f.Eq(x => x.LockoutCount, current.LockoutCount);
            var lockFilter = f.And(
                f.Eq(x => x.ItemId, userId),
                f.Gte(counter.Count, lockThreshold),
                NotLockedFilter(nowUtc),
                sameLockoutCount);

            var lockUpdate = LockUpdate(current, lockDurationInMinutes, nowUtc, userId);

            return await _dataAccess.FindOneAndUpdateAsync(
                collection,
                IamSchemas.User,
                lockFilter,
                lockUpdate,
                $"AuthenticationRepository.LockAccountIf{counter.Name}ThresholdReachedAsync",
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });
        }

        public async Task<bool> ResetFailureCountersAfterSuccessAsync(User snapshot, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            if (snapshot.FailedLoginCount <= 0
                && !snapshot.LastFailedLoginUtc.HasValue
                && snapshot.FailedMfaCount <= 0
                && !snapshot.LastFailedMfaUtc.HasValue
                && !snapshot.LockoutUntilUtc.HasValue)
            {
                return false;
            }

            // The success already gave its own slot back, so a higher count means a wrong password was counted
            // after the snapshot. Never clears an active lock.
            var f = Builders<User>.Filter;
            var filter = f.And(
                f.Eq(x => x.ItemId, snapshot.ItemId),
                NotLockedFilter(nowUtc),
                f.Or(
                    f.Lte(x => x.FailedLoginCount, Math.Max(0, snapshot.FailedLoginCount)),
                    f.Exists(x => x.FailedLoginCount, false)));

            var update = Builders<User>.Update
                .Set(x => x.FailedLoginCount, 0)
                .Set(x => x.LastFailedLoginUtc, null)
                .Set(x => x.FailedMfaCount, 0)
                .Set(x => x.LastFailedMfaUtc, null)
                .Set(x => x.LockoutUntilUtc, null)
                .Set(x => x.LockoutCount, 0)
                .Set(x => x.LastUpdatedDate, nowUtc)
                .Set(x => x.LastUpdatedBy, snapshot.ItemId);

            var reset = await _dataAccess.FindOneAndUpdateAsync(
                GetCollection<User>(),
                IamSchemas.User,
                filter,
                update,
                "AuthenticationRepository.ResetFailureCountersAfterSuccessAsync");
            return reset != null;
        }

        private static FilterDefinition<User> NotLockedFilter(DateTime nowUtc) =>
            Builders<User>.Filter.Or(
                Builders<User>.Filter.Eq(x => x.LockoutUntilUtc, null),
                Builders<User>.Filter.Lte(x => x.LockoutUntilUtc, nowUtc));

        /// <summary>Longest single lock, unless the tenant's base lock time is already longer.</summary>
        public const int MaxLockoutDurationInMinutes = 4 * 60;

        /// <summary>After this many quiet days the lock ladder starts again from the first step.</summary>
        public const int LockoutLadderResetDays = 7;

        /// <summary>
        /// Applies the next lock. Each lock in a row adds the tenant's base lock time (15, 30, 45 …
        /// minutes with the default), up to <see cref="MaxLockoutDurationInMinutes"/>. Both failure
        /// counters start again from zero, so the next lock needs a full new round of wrong attempts.
        /// </summary>
        private static UpdateDefinition<User> LockUpdate(User current, int baseDurationMinutes, DateTime nowUtc, string userId)
        {
            var previousLocks = PreviousLocksInLadder(current.LockoutCount, current.LastLockoutUtc, nowUtc);

            return Builders<User>.Update
                .Set(x => x.LockoutUntilUtc, nowUtc.AddMinutes(LockoutDurationMinutes(previousLocks, baseDurationMinutes)))
                .Set(x => x.LockoutCount, previousLocks + 1)
                .Set(x => x.LastLockoutUtc, nowUtc)
                .Set(x => x.FailedLoginCount, 0)
                .Set(x => x.FailedMfaCount, 0)
                .Set(x => x.LastUpdatedDate, nowUtc)
                .Set(x => x.LastUpdatedBy, userId);
        }

        public static int PreviousLocksInLadder(int lockoutCount, DateTime? lastLockoutUtc, DateTime nowUtc) =>
            lastLockoutUtc.HasValue && (nowUtc - lastLockoutUtc.Value).TotalDays >= LockoutLadderResetDays
                ? 0
                : Math.Max(0, lockoutCount);

        public static int LockoutDurationMinutes(int previousLocks, int baseDurationMinutes)
        {
            var step = Math.Max(1, baseDurationMinutes);
            var cap = Math.Max(step, MaxLockoutDurationInMinutes);
            return (int)Math.Min((long)step * (previousLocks + 1), cap);
        }

        public async Task<List<IdentityProvider>> GetIdentityProvidersAsync()
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Where(_ => true);
            return await _dataAccess.FindListAsync(collection, IamSchemas.IdentityProvider, filter, "AuthenticationRepository.GetIdentityProvidersAsync");
        }


        public async Task<List<IdentityProvider>> GetIdentityProvidersForViewAsync()
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Where(_ => true);
            return await _dataAccess.FindListForViewAsync(collection, IamSchemas.IdentityProvider, filter, "AuthenticationRepository.GetIdentityProvidersForViewAsync");
        }

        public async Task<IdentityProvider?> GetIdentityProviderAsync(string provider)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.Provider, provider);
            return await _dataAccess.FindFirstAsync(collection, IamSchemas.IdentityProvider, filter, "AuthenticationRepository.GetIdentityProviderAsync");
        }

        public async Task<IdentityProvider?> GetIdentityProviderByClientIdAsync(string clientId)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.ClientId, clientId);
            return await _dataAccess.FindFirstAsync(collection, IamSchemas.IdentityProvider, filter, "AuthenticationRepository.GetIdentityProviderByClientIdAsync");
        }

        public async Task<IdentityProvider?> GetIdentityProviderByClientIdAndRedirectUriAsync(string clientId, string redirectUri)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.And(
                Builders<IdentityProvider>.Filter.Eq(x => x.ClientId, clientId),
                Builders<IdentityProvider>.Filter.AnyEq(x => x.RedirectUris, redirectUri));
            return await _dataAccess.FindFirstAsync(collection, IamSchemas.IdentityProvider, filter, "AuthenticationRepository.GetIdentityProviderByClientIdAndRedirectUriAsync");
        }

        public async Task<IdentityProvider?> GetIdentityProviderAsync(string provider, string providerType)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.And(
                Builders<IdentityProvider>.Filter.Eq(x => x.Provider, provider),
                Builders<IdentityProvider>.Filter.Eq(x => x.ProviderType, providerType));
            return await _dataAccess.FindFirstAsync(collection, IamSchemas.IdentityProvider, filter, "AuthenticationRepository.GetIdentityProviderAsync");
        }

        public async Task<IdentityProvider?> GetIdentityProviderByIdAsync(string id)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.ItemId, id);
            return await _dataAccess.FindFirstAsync(collection, IamSchemas.IdentityProvider, filter, "AuthenticationRepository.GetIdentityProviderByIdAsync");
        }


        public async Task<IdentityProvider?> GetIdentityProviderByIdForViewAsync(string id)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.ItemId, id);
            return await _dataAccess.FindFirstForViewAsync(collection, IamSchemas.IdentityProvider, filter, "AuthenticationRepository.GetIdentityProviderByIdForViewAsync");
        }

        public async Task<IdentityProvider> CreateIdentityProviderAsync(IdentityProvider provider)
        {
            await PopulateProviderEndpointsFromWellKnownAsync(provider);

            provider.ItemId = Guid.NewGuid().ToString();
            provider.CreatedDate = DateTime.UtcNow;
            provider.CreatedBy = BlocksContext.GetContext()?.UserId ?? "system";
            provider.LastUpdatedDate = DateTime.UtcNow;
            provider.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "system";

            var collection = GetCollection<IdentityProvider>();
            await _dataAccess.InsertOneAsync(collection, IamSchemas.IdentityProvider, provider, "AuthenticationRepository.CreateIdentityProviderAsync");
            return provider;
        }

        private async Task PopulateProviderEndpointsFromWellKnownAsync(IdentityProvider provider)
        {
            if (provider == null)
            {
                return;
            }

            OpenIdConnectConfiguration? metadata = null;

            if (!string.IsNullOrWhiteSpace(provider.WellKnownUrl))
            {
                metadata = await _oidcDiscoveryClient.GetMetadataAsync(provider.WellKnownUrl);
                provider.AuthorizationUrl = metadata?.AuthorizationEndpoint;
                provider.TokenUrl = metadata?.TokenEndpoint;
                provider.UserInfoUrl = metadata?.UserInfoEndpoint;
                provider.JwksUri = metadata?.JwksUri;
                provider.Issuer = metadata?.Issuer;
            }

            else
            {
                var socialMetadata = GetSocialMetadata(provider.Provider);

                provider.WellKnownUrl = socialMetadata?.WellKnownUrl;
                provider.AuthorizationUrl = socialMetadata?.AuthorizationUrl;
                provider.TokenUrl = socialMetadata?.TokenUrl;
                provider.UserInfoUrl = socialMetadata?.UserInfoUri;
                provider.Scope = SocialLoginScopes;
            }
        }

        private static SocialMetadata? GetSocialMetadata(string? providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return null;
            }

            var normalizedProviderName = providerName.Trim().ToLowerInvariant();

            return normalizedProviderName switch
            {
                var value when value.Contains("google") => new SocialMetadata(
                    "https://accounts.google.com/.well-known/openid-configuration",
                    "https://accounts.google.com/o/oauth2/v2/auth",
                    "https://oauth2.googleapis.com/token",
                    "https://www.googleapis.com/oauth2/v1/userinfo?access_token={0}"
                    ),
                var value when value.Contains("microsoft") => new SocialMetadata(
                    "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration",
                    "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
                    "https://login.microsoftonline.com/common/oauth2/v2.0/token",
                    "https://graph.microsoft.com/v1.0/me"),
                _ => null
            };
        }

        private sealed record SocialMetadata(string WellKnownUrl, string AuthorizationUrl, string TokenUrl, string UserInfoUri);

        public async Task<IdentityProvider> UpdateIdentityProviderAsync(IdentityProvider provider)
        {
            provider.LastUpdatedDate = DateTime.UtcNow;
            provider.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "system";

            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.ItemId, provider.ItemId);
            var options = new ReplaceOptions { IsUpsert = false };
            await _dataAccess.ReplaceOneAsync(collection, IamSchemas.IdentityProvider, filter, provider, "AuthenticationRepository.UpdateIdentityProviderAsync", options);
            return provider;
        }

        public async Task DeleteIdentityProviderAsync(string id)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.ItemId, id);
            await _dataAccess.DeleteOneAsync(collection, IamSchemas.IdentityProvider, filter, "AuthenticationRepository.DeleteIdentityProviderAsync");
        }

        public async Task UpdatePartialAsync<T>(string id, Dictionary<string, object> updates, string collectionName = "")
        {
            IMongoCollection<T> collection = string.IsNullOrWhiteSpace(collectionName) ? GetCollection<T>() : GetCollectionByName<T>(collectionName);

            FilterDefinition<T> filter = Builders<T>.Filter.Eq("_id", id);
            var updateDefinition = new List<UpdateDefinition<T>>();

            foreach (var update in updates)
            {
                updateDefinition.Add(Builders<T>.Update.Set(update.Key, update.Value));
            }

            var combinedUpdate = Builders<T>.Update.Combine(updateDefinition);
            var schema = IamSchemas.For(typeof(T));
            if (schema is not null)
            {
                var edit = await _dataAccess.EditFilterAsync(schema, filter, "AuthenticationRepository.UpdatePartialAsync");
                if (edit is null) return;
                await _dataAccess.CheckUpdateAsync(schema, combinedUpdate, "AuthenticationRepository.UpdatePartialAsync");
                filter = edit;
            }
            await collection.UpdateOneAsync(filter, combinedUpdate);
        }

        public async Task<IdentityConfiguration> GetAuthenticationConfigurationAsync()
        {
            var collection = GetCollection<IdentityConfiguration>();
            var filter = Builders<IdentityConfiguration>.Filter.Where(_ => true);
            return (await _dataAccess.FindFirstAsync(collection, IamSchemas.IdentityConfiguration, filter, "AuthenticationRepository.GetAuthenticationConfigurationAsync"))!;
        }


        public async Task<IdentityConfiguration> GetAuthenticationConfigurationForViewAsync()
        {
            var collection = GetCollection<IdentityConfiguration>();
            var filter = Builders<IdentityConfiguration>.Filter.Where(_ => true);
            return (await _dataAccess.FindFirstForViewAsync(collection, IamSchemas.IdentityConfiguration, filter, "AuthenticationRepository.GetAuthenticationConfigurationForViewAsync"))!;
        }

        public async Task UpdateAuthenticationConfigurationAsync(IdentityConfiguration authenticationConfiguration)
        {
            var collection = GetCollection<IdentityConfiguration>();
            var filter = Builders<IdentityConfiguration>.Filter.Eq("_id", authenticationConfiguration.ItemId);

            // $set of every mapped field, not a whole-document replace: a replace deletes each field the class
            // does not map -- the merged TenantConfiguration field, and on the root tenant AllowedApis.
            var fields = authenticationConfiguration.ToBsonDocument();
            fields.Remove("_id");
            await _dataAccess.UpdateOneAsync(
                collection,
                IamSchemas.IdentityConfiguration,
                filter,
                new BsonDocumentUpdateDefinition<IdentityConfiguration>(new BsonDocument("$set", fields)),
                "AuthenticationRepository.UpdateAuthenticationConfigurationAsync");
        }

        public async Task<OidcClientRegistration> GetOidcClientRegistrationAsync(string clientId)
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            var filter = Builders<OidcClientRegistration>.Filter.Eq(x => x.ItemId, clientId);
            return (await _dataAccess.FindFirstAsync(collection, IamSchemas.OidcClientRegistration, filter, "AuthenticationRepository.GetOidcClientRegistrationAsync"))!;
        }

        public async Task SaveOidcClientRegistrationAsync(OidcClientRegistration credential)
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            await _dataAccess.ReplaceOneAsync(collection, IamSchemas.OidcClientRegistration, Builders<OidcClientRegistration>.Filter.Where(x => x.ItemId == credential.ItemId), credential, "AuthenticationRepository.SaveOidcClientRegistrationAsync", new ReplaceOptions { IsUpsert = true });
        }

        public async Task<OidcUiTemplate?> GetOidcUiTemplateAsync()
        {
            var template = await _keyValueStore.GetAsync<OidcUiTemplate>(OidcUiTemplateStoreKey);
            if (template?.Branding is { } branding)
            {
                var resolvedLight = branding.LogoUrlLight ?? branding.LogoUrlDark ?? branding.LogoUrl;
                var resolvedDark = branding.LogoUrlDark ?? branding.LogoUrlLight ?? branding.LogoUrl;
                branding.LogoUrlLight = resolvedLight;
                branding.LogoUrlDark = resolvedDark;
            }

            return template;
        }

        public Task SaveOidcUiTemplateAsync(OidcUiTemplate template)
        {
            return _keyValueStore.SetAsync(OidcUiTemplateStoreKey, template);
        }

        public async Task<OidcClientRegistration> GetOIDCCredentialByIdAsync(string itemId)
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            var filter = Builders<OidcClientRegistration>.Filter.Eq(it => it.ItemId, itemId);
            return (await _dataAccess.FindFirstForViewAsync(collection, IamSchemas.OidcClientRegistration, filter, "AuthenticationRepository.GetOIDCCredentialByIdAsync"))!;
        }

        public async Task<List<OidcClientRegistration>> GetOIDCCredentialsByTenantAsync()
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            var filter = Builders<OidcClientRegistration>.Filter.Empty;
            return await _dataAccess.FindListAsync(collection, IamSchemas.OidcClientRegistration, filter, "AuthenticationRepository.GetOIDCCredentialsByTenantAsync");
        }


        public async Task<List<OidcClientRegistration>> GetOIDCCredentialsByTenantForViewAsync()
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            var filter = Builders<OidcClientRegistration>.Filter.Empty;
            return await _dataAccess.FindListForViewAsync(collection, IamSchemas.OidcClientRegistration, filter, "AuthenticationRepository.GetOIDCCredentialsByTenantForViewAsync");
        }

        public async Task DeleteOidcClientAsync(DeleteOIDCClientRequest request)
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            var filter = Builders<OidcClientRegistration>.Filter.Eq(it => it.ItemId, request.ItemId);
            await _dataAccess.DeleteOneAsync(collection, IamSchemas.OidcClientRegistration, filter, "AuthenticationRepository.DeleteOidcClientAsync");
        }

        public async Task<ClientCredential> GetClientCredentialByIdAsync(string clientId)
        {
            var collection = GetCollection<ClientCredential>();
            var filter = Builders<ClientCredential>.Filter.Eq(it => it.ItemId, clientId);
            return (await _dataAccess.FindFirstAsync(collection, IamSchemas.ClientCredential, filter, "AuthenticationRepository.GetClientCredentialByIdAsync"))!;
        }

        public async Task<BiometricCredential> AuthenticateBiometricCredentialAsync(string biometricId, string biometricKey)
        {
            var collection = GetCollection<BiometricCredential>();
            var filter = Builders<BiometricCredential>.Filter.Where(it => it.BiometricId == biometricId && it.BiometricKey == biometricKey);
            return (await _dataAccess.FindFirstAsync(collection, IamSchemas.BiometricCredential, filter, "AuthenticationRepository.AuthenticateBiometricCredentialAsync"))!;
        }

        public async Task<BlocksClientConfig> GetBlocksClientAsync(string clientId)
        {
            var collection = GetCollection<BlocksClientConfig>();
            var filter = Builders<BlocksClientConfig>.Filter.Eq(it => it.ItemId, clientId);
            return (await _dataAccess.FindFirstAsync(collection, IamSchemas.BlocksClientConfig, filter, "AuthenticationRepository.GetBlocksClientAsync"))!;
        }

        public async Task<BaseResponse> SaveClientCredentialAsync(ClientCredential clientCredential)
        {
            var collection = GetCollection<ClientCredential>();
            var result = await _dataAccess.ReplaceOneAsync(collection, IamSchemas.ClientCredential, Builders<ClientCredential>.Filter.Where(x => x.ItemId == clientCredential.ItemId), clientCredential, "AuthenticationRepository.SaveClientCredentialAsync", new ReplaceOptions { IsUpsert = true });
            return new BaseResponse { IsSuccess = result?.IsAcknowledged ?? false };
        }

        public async Task DeleteClientCredentialAsync(DeleteClientCredentialRequest request)
        {
            var collection = GetCollection<ClientCredential>();
            var filter = Builders<ClientCredential>.Filter.Eq(it => it.ItemId, request.ItemId);
            await _dataAccess.DeleteOneAsync(collection, IamSchemas.ClientCredential, filter, "AuthenticationRepository.DeleteClientCredentialAsync");
        }

        /// <param name="organizationId">
        /// The organization to restrict the list to, or null for every organization in the tenant.
        /// Null is the tenant-wide scope and must only be passed for a caller that resolved to it.
        /// </param>
        public async Task<List<ClientCredential>> GetClientCredentialsAsync(string? organizationId)
        {
            var collection = GetCollection<ClientCredential>();
            var filter = organizationId == null
                ? Builders<ClientCredential>.Filter.Where(_ => true)
                : Builders<ClientCredential>.Filter.Eq(it => it.OrganizationId, organizationId);
            return await _dataAccess.FindListForViewAsync(collection, IamSchemas.ClientCredential, filter, "AuthenticationRepository.GetClientCredentialsAsync");
        }

        // Impersonation session methods
        public async Task<bool> InsertImpersonationSessionAsync(ImpersonationSession session)
        {
            try
            {
                var collection = GetCollection<ImpersonationSession>();
                await _dataAccess.InsertOneAsync(collection, IamSchemas.ImpersonationSession, session, "AuthenticationRepository.InsertImpersonationSessionAsync");
                return true;
            }
            catch (Exception ex)
            {
                // Log exception
                return false;
            }
        }

        public async Task<ImpersonationSession?> GetImpersonationSessionByIdAsync(string sessionId)
        {
            var collection = GetCollection<ImpersonationSession>();
            var filter = Builders<ImpersonationSession>.Filter.Eq(x => x.Id, sessionId);
            return await _dataAccess.FindFirstAsync(collection, IamSchemas.ImpersonationSession, filter, "AuthenticationRepository.GetImpersonationSessionByIdAsync");
        }

        public async Task<List<ImpersonationSession>> GetActiveImpersonationSessionsByUserIdAsync(string userId)
        {
            var collection = GetCollection<ImpersonationSession>();
            var filter = Builders<ImpersonationSession>.Filter.And(
                Builders<ImpersonationSession>.Filter.Eq(x => x.UserId, userId),
                Builders<ImpersonationSession>.Filter.Eq(x => x.Status, "active")
            );
            return await _dataAccess.FindListAsync(collection, IamSchemas.ImpersonationSession, filter, "AuthenticationRepository.GetActiveImpersonationSessionsByUserIdAsync");
        }

        public async Task<bool> UpdateImpersonationSessionAsync(string sessionId, Dictionary<string, object> updates)
        {
            try
            {
                var collection = GetCollection<ImpersonationSession>();
                var filter = Builders<ImpersonationSession>.Filter.Eq(x => x.Id, sessionId);

                var updateDefinition = Builders<ImpersonationSession>.Update;
                var updateList = new List<UpdateDefinition<ImpersonationSession>>();

                foreach (var kvp in updates)
                {
                    updateList.Add(updateDefinition.Set(kvp.Key, kvp.Value));
                }

                updateList.Add(updateDefinition.Set(x => x.UpdateDate, DateTime.UtcNow));

                var combinedUpdate = updateDefinition.Combine(updateList);
                var result = await _dataAccess.UpdateOneAsync(collection, IamSchemas.ImpersonationSession, filter, combinedUpdate, "AuthenticationRepository.UpdateImpersonationSessionAsync");

                return result is not null && result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                // Log exception
                return false;
            }
        }

        private static string NormalizeIdentity(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

    }
}
