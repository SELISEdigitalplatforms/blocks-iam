using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.ResponseModel;
using Authentication.DomainService.Shared.Services;
using Iam.DomainService.Entities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MongoDB.Driver;

namespace Authentication.DomainService.Services
{
    public sealed class AuthenticationRepository : IAuthenticationRepository
    {
        private const string OidcClientRegistrationsCollectionName = "OidcClientRegistrations";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly OidcDiscoveryClient _oidcDiscoveryClient;

        public AuthenticationRepository(IDbContextProvider dbContextProvider, OidcDiscoveryClient oidcDiscoveryClient)
        {
            _dbContextProvider = dbContextProvider;
            _oidcDiscoveryClient = oidcDiscoveryClient;
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
            var options = new FindOptions<User>
            {
                Collation = new Collation("en", strength: CollationStrength.Secondary)
            };
            var filter = Builders<User>.Filter.Eq(x => x.Email, email);
            return await (await collection.FindAsync(filter, options)).FirstOrDefaultAsync();
        }

        public async Task<User> GetUserByUsernameAsync(string username, string? organizationId = null)
        {
            var collection = GetCollection<User>();

            var filter = Builders<User>.Filter.Eq(u => u.UserName, username) | Builders<User>.Filter.Eq(u => u.Email, username);

            if (!string.IsNullOrWhiteSpace(organizationId) && organizationId != "default")
            {

                filter &= Builders<User>.Filter.AnyEq("OrganizationIds", organizationId);
            }

            return await collection.Find(filter).FirstOrDefaultAsync();
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

        public async Task<bool> InsertIdentitySessionAsync(IdentitySession session)
        {
            var collection = GetCollection<IdentitySession>();
            await collection.InsertOneAsync(session);
            return true;
        }

        public async Task<bool> InsertIdentityEventAsync(IdentityEvent identityEvent)
        {
            var collection = GetCollection<IdentityEvent>();
            await collection.InsertOneAsync(identityEvent);
            return true;
        }

        public async Task<bool> InsertUserAuthenticationTimelineAsync(UserAuthenticationTimeline userAuthenticationTimeline)
        {
            var collection = GetCollection<UserAuthenticationTimeline>();
            await collection.InsertOneAsync(userAuthenticationTimeline);
            return true;
        }

        public async Task<bool> RevokeIdentitySessionAsync(string refreshToken, string userId)
        {
            var collection = GetCollection<IdentitySession>();
            var update = Builders<IdentitySession>.Update.Set(x => x.IsActive, false)
                .Set(x => x.ExpiresUtc, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            var result = await collection.UpdateManyAsync(x => x.RefreshToken == refreshToken && x.UserId == userId, update);
            return result.IsAcknowledged;
        }

        public async Task<bool> RevokeIdentitySessionsByRefreshTokensAsync(IEnumerable<string> refreshTokens)
        {
            var collection = GetCollection<IdentitySession>();
            var update = Builders<IdentitySession>.Update.Set(x => x.IsActive, false)
                .Set(x => x.ExpiresUtc, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            var filter = Builders<IdentitySession>.Filter.In(x => x.RefreshToken, refreshTokens);
            var result = await collection.UpdateManyAsync(filter, update);
            return result.IsAcknowledged;
        }

        public async Task<bool> RevokeIdentitySessionsByUserIdAsync(string userId)
        {
            var collection = GetCollection<IdentitySession>();
            var update = Builders<IdentitySession>.Update.Set(x => x.IsActive, false)
                .Set(x => x.ExpiresUtc, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            var filter = Builders<IdentitySession>.Filter.Eq(x => x.UserId, userId);
            var result = await collection.UpdateManyAsync(filter, update);
            return result.IsAcknowledged;
        }
        public async Task<bool> RevokeIdentitySessionsBySessionIdsAsync(IEnumerable<string> sessionIds)
        {
            var collection = GetCollection<IdentitySession>();
            var update = Builders<IdentitySession>.Update.Set(x => x.IsActive, false)
                .Set(x => x.ExpiresUtc, DateTime.UtcNow)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);
            var filter = Builders<IdentitySession>.Filter.In(x => x.SessionId, sessionIds);
            var result = await collection.UpdateManyAsync(filter, update);
            return result.IsAcknowledged;
        }

        public async Task<bool> UpdateSessionStatusForAllRefreshTokenAsync(List<string> refreshTokens)
        {
            if (refreshTokens == null || refreshTokens.Count == 0)
            {
                return true;
            }

            return await RevokeIdentitySessionsByRefreshTokensAsync(refreshTokens);
        }

        public async Task<IEnumerable<IdentitySession>> GetActiveIdentitySessionByUserIdAsync(string userId)
        {
            var collection = GetCollection<IdentitySession>();
            var filter = Builders<IdentitySession>.Filter.Eq(x => x.UserId, userId) & Builders<IdentitySession>.Filter.Eq(x => x.IsActive, true);
            return await collection.Find(filter).ToListAsync();
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

            var userAfterIncrement = await collection.FindOneAndUpdateAsync(
                incrementFilter,
                incrementUpdate,
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

            var userAfterLock = await collection.FindOneAndUpdateAsync(
                lockFilter,
                lockUpdate,
                new FindOneAndUpdateOptions<User>
                {
                    ReturnDocument = ReturnDocument.After
                });

            return userAfterLock ?? userAfterIncrement;
        }

        public async Task<User?> IncrementFailedMfaAndApplyLockoutAsync(string userId, int lockThreshold, int lockDurationInMinutes, DateTime nowUtc)
        {
            var collection = GetCollection<User>();

            var incrementFilter = Builders<User>.Filter.Eq(x => x.ItemId, userId);
            var incrementUpdate = Builders<User>.Update
                .Inc(x => x.FailedMfaCount, 1)
                .Set(x => x.LastFailedMfaUtc, nowUtc)
                .Set(x => x.LastUpdatedDate, nowUtc)
                .Set(x => x.LastUpdatedBy, userId);

            var userAfterIncrement = await collection.FindOneAndUpdateAsync(
                incrementFilter,
                incrementUpdate,
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

            var userAfterLock = await collection.FindOneAndUpdateAsync(
                lockFilter,
                lockUpdate,
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

        public async Task<IdentitySession?> GetIdentitySessionByRefreshTokenAsync(string refreshToken)
        {
            var collection = GetCollection<IdentitySession>();
            var filter = Builders<IdentitySession>.Filter.Eq(x => x.RefreshToken, refreshToken);
            return await collection.Find(filter).SortByDescending(x => x.UpdatedAt).FirstOrDefaultAsync();
        }

        public async Task<bool> UpsertIdentitySessionBySessionIdAsync(IdentitySession session)
        {
            var collection = GetCollectionByName<IdentitySession>("IdentitySessions");
            var b = Builders<IdentitySession>.Filter;

            FilterDefinition<IdentitySession> filter;
            if (!string.IsNullOrWhiteSpace(session.SessionId))
            {
                filter = b.And(
                    b.Eq(x => x.UserId, session.UserId),
                    b.Eq(x => x.TenantId, session.TenantId),
                    b.Eq(x => x.SessionId, session.SessionId)
                );
            }
            else
            {
                filter = b.And(
                    b.Eq(x => x.UserId, session.UserId),
                    b.Eq(x => x.TenantId, session.TenantId),
                    b.Eq(x => x.ClientId, session.ClientId ?? string.Empty),
                    b.Eq(x => x.DeviceInformation!.Device, session.DeviceInformation?.Device ?? string.Empty)
                );
            }

            var setOnInsert = Builders<IdentitySession>.Update
                .SetOnInsert(x => x.TenantId, session.TenantId)
                .SetOnInsert(x => x.UserId, session.UserId)
                .SetOnInsert(x => x.OrganizationId, session.OrganizationId)
                .SetOnInsert(x => x.ClientId, session.ClientId)
                .SetOnInsert(x => x.SessionId, session.SessionId)
                .SetOnInsert(x => x.IssuedUtc, session.IssuedUtc)
                .SetOnInsert(x => x.CreatedAt, DateTime.UtcNow)
                .SetOnInsert(x => x.IsLogin, session.IsLogin);

            var setMutables = Builders<IdentitySession>.Update
                .Set(x => x.RefreshToken, session.RefreshToken)
                .Set(x => x.ExpiresUtc, session.ExpiresUtc)
                .Set(x => x.UpdatedAt, DateTime.UtcNow)
                .Set(x => x.IsActive, true)
                .Set(x => x.IpAddresses, session.IpAddresses)
                .Set(x => x.DeviceInformation, session.DeviceInformation)
                .Set(x => x.GrantType, session.GrantType);

            var update = Builders<IdentitySession>.Update.Combine(setOnInsert, setMutables);

            var options = new UpdateOptions { IsUpsert = true };
            var result = await collection.UpdateOneAsync(filter, update, options);
            return result.IsAcknowledged;
        }

        public async Task<IEnumerable<IdentitySession>> GetActiveIdentitySessionBySessionIdAsync(string sessionId)
        {
            var collection = GetCollection<IdentitySession>();
            var filter = Builders<IdentitySession>.Filter.Eq(x => x.SessionId, sessionId) & Builders<IdentitySession>.Filter.Eq(x => x.IsActive, true);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<List<IdentityProvider>> GetIdentityProvidersAsync()
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Where(_ => true);
            var cursor = await collection.FindAsync(filter);
            return await cursor.ToListAsync();
        }

        public async Task<IdentityProvider?> GetIdentityProviderAsync(string provider)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.Provider, provider);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IdentityProvider?> GetIdentityProviderByClientIdAsync(string clientId)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.ClientId, clientId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IdentityProvider?> GetIdentityProviderByClientIdAndRedirectUriAsync(string clientId, string redirectUri)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.And(
                Builders<IdentityProvider>.Filter.Eq(x => x.ClientId, clientId),
                Builders<IdentityProvider>.Filter.AnyEq(x => x.RedirectUris, redirectUri));
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IdentityProvider?> GetIdentityProviderAsync(string provider, string providerType)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.And(
                Builders<IdentityProvider>.Filter.Eq(x => x.Provider, provider),
                Builders<IdentityProvider>.Filter.Eq(x => x.ProviderType, providerType));
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IdentityProvider?> GetIdentityProviderByIdAsync(string id)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.ItemId, id);
            return await collection.Find(filter).FirstOrDefaultAsync();
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
            await collection.InsertOneAsync(provider);
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

                provider.WellKnownUrl ??= socialMetadata?.WellKnownUrl;
                provider.AuthorizationUrl ??= socialMetadata?.AuthorizationUrl;
                provider.TokenUrl ??= socialMetadata?.TokenUrl;
            }
        }

        private static string? GetDefaultWellKnownUrl(string? providerName)
        {
            return GetSocialMetadata(providerName)?.WellKnownUrl;
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
                    "https://oauth2.googleapis.com/token"),
                var value when value.Contains("microsoft") => new SocialMetadata(
                    "https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration",
                    "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
                    "https://login.microsoftonline.com/common/oauth2/v2.0/token"),
                _ => null
            };
        }

        private sealed record SocialMetadata(string WellKnownUrl, string AuthorizationUrl, string TokenUrl);

        public async Task<IdentityProvider> UpdateIdentityProviderAsync(IdentityProvider provider)
        {
            provider.LastUpdatedDate = DateTime.UtcNow;
            provider.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? "system";

            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.ItemId, provider.ItemId);
            var options = new ReplaceOptions { IsUpsert = false };
            await collection.ReplaceOneAsync(filter, provider, options);
            return provider;
        }

        public async Task DeleteIdentityProviderAsync(string id)
        {
            var collection = GetCollection<IdentityProvider>();
            var filter = Builders<IdentityProvider>.Filter.Eq(x => x.ItemId, id);
            await collection.DeleteOneAsync(filter);
        }

        public async Task UpdatePartialAsync<T>(string id, Dictionary<string, object> updates, string collectionName = "")
        {
            IMongoCollection<T> collection = string.IsNullOrWhiteSpace(collectionName) ? GetCollection<T>() : GetCollection<T>(collectionName);

            var filter = Builders<T>.Filter.Eq("_id", id);
            var updateDefinition = new List<UpdateDefinition<T>>();

            foreach (var update in updates)
            {
                updateDefinition.Add(Builders<T>.Update.Set(update.Key, update.Value));
            }

            var combinedUpdate = Builders<T>.Update.Combine(updateDefinition);
            await collection.UpdateOneAsync(filter, combinedUpdate);
        }

        public async Task<IdentityConfiguration> GetAuthenticationConfigurationAsync()
        {
            var collection = GetCollection<IdentityConfiguration>();
            var filter = Builders<IdentityConfiguration>.Filter.Where(_ => true);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task UpdateAuthenticationConfigurationAsync(IdentityConfiguration authenticationConfiguration)
        {
            var collection = GetCollection<IdentityConfiguration>();
            var filter = Builders<IdentityConfiguration>.Filter.Eq("_id", authenticationConfiguration.ItemId);
            await collection.ReplaceOneAsync(filter, authenticationConfiguration);
        }

        public async Task<OidcClientRegistration> GetOidcClientRegistrationAsync(string clientId)
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            var filter = Builders<OidcClientRegistration>.Filter.Eq(x => x.ItemId, clientId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task SaveOidcClientRegistrationAsync(OidcClientRegistration credential)
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            var result = await collection.ReplaceOneAsync(x => x.ItemId == credential.ItemId, credential, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<OidcClientRegistration> GetOIDCCredentialByIdAsync(string itemId)
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            var filter = Builders<OidcClientRegistration>.Filter.Eq(it => it.ItemId, itemId);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<List<OidcClientRegistration>> GetOIDCCredentialsByTenantAsync()
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            var filter = Builders<OidcClientRegistration>.Filter.Empty;
            return await (await collection.FindAsync(filter)).ToListAsync();
        }

        public async Task DeleteOidcClientAsync(DeleteOIDCClientRequest request)
        {
            var collection = GetCollectionByName<OidcClientRegistration>(OidcClientRegistrationsCollectionName);
            var filter = Builders<OidcClientRegistration>.Filter.Eq(it => it.ItemId, request.ItemId);
            await collection.DeleteOneAsync(filter);
        }

        public async Task<ClientCredential> GetClientCredentialByIdAsync(string clientId)
        {
            var collection = GetCollection<ClientCredential>();
            var filter = Builders<ClientCredential>.Filter.Eq(it => it.ItemId, clientId);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<BiometricCredential> AuthenticateBiometricCredentialAsync(string biometricId, string biometricKey)
        {
            var collection = GetCollection<BiometricCredential>();
            var filter = Builders<BiometricCredential>.Filter.Where(it => it.BiometricId == biometricId && it.BiometricKey == biometricKey);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<UserCode> GetUserCodeAsync(string code)
        {
            var collection = GetCollection<UserCode>();
            var filter = Builders<UserCode>.Filter.Eq(it => it.Code, code);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task<BlocksClientConfig> GetBlocksClientAsync(string clientId)
        {
            var collection = GetCollection<BlocksClientConfig>();
            var filter = Builders<BlocksClientConfig>.Filter.Eq(it => it.ItemId, clientId);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task SaveUserCodeByClientAsync(UserCode userCode)
        {
            var collection = GetCollection<UserCode>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == userCode.ItemId, userCode, new ReplaceOptions { IsUpsert = true });
        }

        public async Task<List<GetUserCodesByUserIdResponse>> GetUserCodesByUserIdAsync(string userId)
        {
            var collection = GetCollection<UserCode>();
            var filter = Builders<UserCode>.Filter.Eq(it => it.UserId, userId);
            var userCodes = await (await collection.FindAsync(filter)).ToListAsync();
            return GetUserCodesByUserIdResponse(userCodes);
        }

        private List<GetUserCodesByUserIdResponse> GetUserCodesByUserIdResponse(List<UserCode> userCodes)
        {
            return userCodes.Select(x => new GetUserCodesByUserIdResponse
            {
                ItemId = x.ItemId,
                CreatedDate = x.CreatedDate,
                UserId = x.UserId,
                Code = x.Code,
                ExpiryDate = x.CodeTtlInMinute.HasValue ? x.CreatedDate.AddMinutes(x.CodeTtlInMinute.Value) : x.CreatedDate,
                CodeTtlInMinute = x.CodeTtlInMinute,
                ClientId = x.ClientId,
                Note = x.Note,
            }).ToList();
        }

        public async Task<BaseResponse> SaveClientCredentialAsync(ClientCredential clientCredential)
        {
            var collection = GetCollection<ClientCredential>();
            var result = await collection.ReplaceOneAsync(x => x.ItemId == clientCredential.ItemId, clientCredential, new ReplaceOptions { IsUpsert = true });
            return new BaseResponse { IsSuccess = result.IsAcknowledged };
        }

        public async Task DeleteClientCredentialAsync(DeleteClientCredentialRequest request)
        {
            var collection = GetCollection<ClientCredential>();
            var filter = Builders<ClientCredential>.Filter.Eq(it => it.ItemId, request.ItemId);
            await collection.DeleteOneAsync(filter);
        }

        public async Task<List<ClientCredential>> GetClientCredentialsAsync()
        {
            var collection = GetCollection<ClientCredential>();
            var filter = Builders<ClientCredential>.Filter.Where(_ => true);
            var cursor = await collection.FindAsync(filter);
            return await cursor.ToListAsync();
        }

        // Impersonation session methods
        public async Task<bool> InsertImpersonationSessionAsync(ImpersonationSession session)
        {
            try
            {
                var collection = GetCollection<ImpersonationSession>();
                await collection.InsertOneAsync(session);
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
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<List<ImpersonationSession>> GetActiveImpersonationSessionsByUserIdAsync(string userId)
        {
            var collection = GetCollection<ImpersonationSession>();
            var filter = Builders<ImpersonationSession>.Filter.And(
                Builders<ImpersonationSession>.Filter.Eq(x => x.UserId, userId),
                Builders<ImpersonationSession>.Filter.Eq(x => x.Status, "active")
            );
            var cursor = await collection.FindAsync(filter);
            return await cursor.ToListAsync();
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
                var result = await collection.UpdateOneAsync(filter, combinedUpdate);

                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                // Log exception
                return false;
            }
        }

    }
}