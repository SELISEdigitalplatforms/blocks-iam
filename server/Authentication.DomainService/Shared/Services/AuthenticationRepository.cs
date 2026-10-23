using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Authentication.DomainService.Shared.Services;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
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

        public async Task<User?> IncrementFailedLoginAndApplyLockoutAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc)
        {
            var collection = GetCollection<User>();

            var incrementFilter = Builders<User>.Filter.Eq(x => x.ItemId, userId);
            var incrementUpdate = Builders<User>.Update
                .Inc(x => x.FailedLoginCount, 1)
                .Set(x => x.LastFailedLoginUtc, nowUtc)
                .Set(x => x.LastUpdatedDate, nowUtc)
                .Set(x => x.LastUpdatedBy, userId);

            var userAfterIncrement = await _dataAccess.FindOneAndUpdateAsync(
                collection,
                IamSchemas.User,
                incrementFilter,
                incrementUpdate,
                "AuthenticationRepository.IncrementFailedLoginAndApplyLockoutAsync",
                new FindOneAndUpdateOptions<User>
                {
                    ReturnDocument = ReturnDocument.After
                });

            if (userAfterIncrement == null)
            {
                return null;
            }

            if (userAfterIncrement.FailedLoginCount < lockThreshold)
            {
                return userAfterIncrement;
            }

            if (userAfterIncrement.LockoutUntilUtc.HasValue && userAfterIncrement.LockoutUntilUtc.Value > nowUtc)
            {
                return userAfterIncrement;
            }

            // Calculate actual lockout duration with exponential backoff
            var actualLockoutDurationInMinutes = CalculateExponentialBackoffLockoutDuration(
                userAfterIncrement.LockoutCount,
                userAfterIncrement.LastLockoutUtc,
                lockDurationInMinutes,
                7); // 7-day reset window

            var lockoutUntilUtc = nowUtc.AddMinutes(actualLockoutDurationInMinutes);
            var lockFilter = Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(x => x.ItemId, userId),
                Builders<User>.Filter.Eq(x => x.FailedLoginCount, userAfterIncrement.FailedLoginCount));

            var lockUpdate = Builders<User>.Update
                .Set(x => x.LockoutUntilUtc, lockoutUntilUtc)
                .Inc(x => x.LockoutCount, 1) // Increment lockout count for next time
                .Set(x => x.LastLockoutUtc, nowUtc)
                .Set(x => x.LastUpdatedDate, nowUtc)
                .Set(x => x.LastUpdatedBy, userId);

            var userAfterLock = await _dataAccess.FindOneAndUpdateAsync(
                collection,
                IamSchemas.User,
                lockFilter,
                lockUpdate,
                "AuthenticationRepository.IncrementFailedLoginAndApplyLockoutAsync.Lock",
                new FindOneAndUpdateOptions<User>
                {
                    ReturnDocument = ReturnDocument.After
                });

            return userAfterLock ?? userAfterIncrement;
        }

        /// <summary>
        /// How long the last free slot stays taken by one reservation once the threshold is reached. Shorter than
        /// any lock (minimum 1 minute) and longer than any request, so a crashed request frees the slot again.
        /// </summary>
        public static readonly TimeSpan LoginAttemptLease = TimeSpan.FromSeconds(60);

        public async Task<User?> TryReserveLoginAttemptAsync(string userId, int lockThreshold, DateTime nowUtc)
        {
            var f = Builders<User>.Filter;
            var filter = f.And(
                f.Eq(x => x.ItemId, userId),
                NotLockedFilter(nowUtc),
                f.Or(
                    f.Lt(x => x.FailedLoginCount, lockThreshold),
                    f.Exists(x => x.FailedLoginCount, false),
                    f.Eq(x => x.LastFailedLoginUtc, null),
                    f.Lte(x => x.LastFailedLoginUtc, nowUtc - LoginAttemptLease)));

            var update = Builders<User>.Update
                .Inc(x => x.FailedLoginCount, 1)
                .Set(x => x.LastFailedLoginUtc, nowUtc)
                .Set(x => x.LastUpdatedDate, nowUtc)
                .Set(x => x.LastUpdatedBy, userId);

            return await _dataAccess.FindOneAndUpdateAsync(
                GetCollection<User>(),
                IamSchemas.User,
                filter,
                update,
                "AuthenticationRepository.TryReserveLoginAttemptAsync",
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });
        }

        public async Task<bool> ReleaseLoginAttemptAsync(string userId, DateTime nowUtc)
        {
            var collection = GetCollection<User>();
            var f = Builders<User>.Filter;
            var released = await _dataAccess.FindOneAndUpdateAsync(
                collection,
                IamSchemas.User,
                f.And(f.Eq(x => x.ItemId, userId), NotLockedFilter(nowUtc), f.Gt(x => x.FailedLoginCount, 0)),
                Builders<User>.Update
                    .Inc(x => x.FailedLoginCount, -1)
                    .Set(x => x.LastUpdatedDate, nowUtc)
                    .Set(x => x.LastUpdatedBy, userId),
                "AuthenticationRepository.ReleaseLoginAttemptAsync");
            if (released != null)
            {
                return true;
            }

            // Nothing to give back (a concurrent sign-in already reset the counter) or the account is locked.
            var current = await _dataAccess.FindFirstAsync(collection, IamSchemas.User, Builders<User>.Filter.Eq(x => x.ItemId, userId), "AuthenticationRepository.ReleaseLoginAttemptAsync.Read");
            return current != null && !(current.LockoutUntilUtc.HasValue && current.LockoutUntilUtc.Value > nowUtc);
        }

        public async Task<User?> LockAccountIfLoginThresholdReachedAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc)
        {
            var collection = GetCollection<User>();
            var current = await _dataAccess.FindFirstAsync(collection, IamSchemas.User, Builders<User>.Filter.Eq(x => x.ItemId, userId), "AuthenticationRepository.LockAccountIfLoginThresholdReachedAsync.Read");
            if (current == null
                || current.FailedLoginCount < lockThreshold
                || (current.LockoutUntilUtc.HasValue && current.LockoutUntilUtc.Value > nowUtc))
            {
                return null;
            }

            var actualLockoutDurationInMinutes = CalculateExponentialBackoffLockoutDuration(
                current.LockoutCount,
                current.LastLockoutUtc,
                lockDurationInMinutes,
                7); // 7-day reset window

            // LockoutCount must still be the value the duration was computed from: no double lock, exact escalation.
            var f = Builders<User>.Filter;
            var sameLockoutCount = current.LockoutCount == 0
                ? f.Or(f.Eq(x => x.LockoutCount, 0), f.Exists(x => x.LockoutCount, false))
                : f.Eq(x => x.LockoutCount, current.LockoutCount);
            var lockFilter = f.And(
                f.Eq(x => x.ItemId, userId),
                f.Gte(x => x.FailedLoginCount, lockThreshold),
                NotLockedFilter(nowUtc),
                sameLockoutCount);

            var lockUpdate = Builders<User>.Update
                .Set(x => x.LockoutUntilUtc, nowUtc.AddMinutes(actualLockoutDurationInMinutes))
                .Inc(x => x.LockoutCount, 1)
                .Set(x => x.LastLockoutUtc, nowUtc)
                .Set(x => x.LastUpdatedDate, nowUtc)
                .Set(x => x.LastUpdatedBy, userId);

            return await _dataAccess.FindOneAndUpdateAsync(
                collection,
                IamSchemas.User,
                lockFilter,
                lockUpdate,
                "AuthenticationRepository.LockAccountIfLoginThresholdReachedAsync",
                new FindOneAndUpdateOptions<User> { ReturnDocument = ReturnDocument.After });
        }

        private static FilterDefinition<User> NotLockedFilter(DateTime nowUtc) =>
            Builders<User>.Filter.Or(
                Builders<User>.Filter.Eq(x => x.LockoutUntilUtc, null),
                Builders<User>.Filter.Lte(x => x.LockoutUntilUtc, nowUtc));

        public async Task<User?> IncrementFailedMfaAndApplyLockoutAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc)
        {
            var collection = GetCollection<User>();

            var incrementFilter = Builders<User>.Filter.Eq(x => x.ItemId, userId);
            var incrementUpdate = Builders<User>.Update
                .Inc(x => x.FailedMfaCount, 1)
                .Set(x => x.LastFailedMfaUtc, nowUtc)
                .Set(x => x.LastUpdatedDate, nowUtc)
                .Set(x => x.LastUpdatedBy, userId);

            var userAfterIncrement = await _dataAccess.FindOneAndUpdateAsync(
                collection,
                IamSchemas.User,
                incrementFilter,
                incrementUpdate,
                "AuthenticationRepository.IncrementFailedMfaAndApplyLockoutAsync",
                new FindOneAndUpdateOptions<User>
                {
                    ReturnDocument = ReturnDocument.After
                });

            if (userAfterIncrement == null)
            {
                return null;
            }

            if (userAfterIncrement.FailedMfaCount < lockThreshold)
            {
                return userAfterIncrement;
            }

            if (userAfterIncrement.LockoutUntilUtc.HasValue && userAfterIncrement.LockoutUntilUtc.Value > nowUtc)
            {
                return userAfterIncrement;
            }

            var actualLockoutDurationInMinutes = CalculateExponentialBackoffLockoutDuration(
                userAfterIncrement.LockoutCount,
                userAfterIncrement.LastLockoutUtc,
                lockDurationInMinutes,
                7);

            var lockoutUntilUtc = nowUtc.AddMinutes(actualLockoutDurationInMinutes);
            var lockFilter = Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(x => x.ItemId, userId),
                Builders<User>.Filter.Eq(x => x.FailedMfaCount, userAfterIncrement.FailedMfaCount));

            var lockUpdate = Builders<User>.Update
                .Set(x => x.LockoutUntilUtc, lockoutUntilUtc)
                .Inc(x => x.LockoutCount, 1)
                .Set(x => x.LastLockoutUtc, nowUtc)
                .Set(x => x.LastUpdatedDate, nowUtc)
                .Set(x => x.LastUpdatedBy, userId);

            var userAfterLock = await _dataAccess.FindOneAndUpdateAsync(
                collection,
                IamSchemas.User,
                lockFilter,
                lockUpdate,
                "AuthenticationRepository.IncrementFailedMfaAndApplyLockoutAsync.Lock",
                new FindOneAndUpdateOptions<User>
                {
                    ReturnDocument = ReturnDocument.After
                });

            return userAfterLock ?? userAfterIncrement;
        }

        /// <summary>
        /// Calculates exponential backoff lockout duration.
        /// - 1st lockout: baseDuration (5 min)
        /// - 2nd lockout: 3x baseDuration (15 min)
        /// - 3rd lockout: 12x baseDuration (60 min)
        /// - 4th+ lockout: 288x baseDuration (24 hours)
        /// Resets counter if last lockout was > resetWindowDays ago.
        /// </summary>
        private int CalculateExponentialBackoffLockoutDuration(
            int currentLockoutCount,
            DateTime? lastLockoutUtc,
            int baseDurationMinutes,
            int resetWindowDays)
        {
            var now = DateTime.UtcNow;

            // Reset counter if last lockout was too long ago
            if (lastLockoutUtc.HasValue && (now - lastLockoutUtc.Value).TotalDays >= resetWindowDays)
            {
                // Reset to 1st lockout duration
                return baseDurationMinutes;
            }

            // Exponential backoff based on lockout count
            return currentLockoutCount switch
            {
                0 => baseDurationMinutes,              // 5 minutes
                1 => baseDurationMinutes * 3,          // 15 minutes
                2 => baseDurationMinutes * 12,         // 60 minutes (1 hour)
                _ => baseDurationMinutes * 288         // 1440 minutes (24 hours)
            };
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
