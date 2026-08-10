using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Entities;

namespace Authentication.DomainService.Authentication
{
    public sealed class TokenRefresher : ITokenRefresher
    {
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        private readonly RefreshTokenAuthenticationService _refreshTokenAuthenticationService;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;

        public TokenRefresher(
            ICacheClient cacheClient,
            ITenants tenants,
            RefreshTokenAuthenticationService refreshTokenAuthenticationService,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager)
        {
            _cacheClient = cacheClient;
            _tenants = tenants;
            _refreshTokenAuthenticationService = refreshTokenAuthenticationService;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
        }

        public Task<string> GetCacheValueAsync(string key)
            => _cacheClient.GetStringValueAsync(key);

        public Task SetCacheValueAsync(string key, string value, int expiryInSeconds)
            => _cacheClient.AddStringValueAsync(key, value, expiryInSeconds);

        public Task RemoveKeyAsync(string key)
            => _cacheClient.RemoveKeyAsync(key);

        public Task<Tenant?> GetTenantByIDAsync(string tenantId)
            => Task.FromResult(_tenants.GetTenantByID(tenantId));

        public Task<TokenResponse> AuthenticateAsync(TokenRequest tokenRequest, IdentityConfiguration authConfiguration, User user)
            => _oAuthJwtAccessTokenManager.ManageTokenAsync(tokenRequest, authConfiguration, user);
    }
}
