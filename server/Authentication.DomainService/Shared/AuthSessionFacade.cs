using Authentication.DomainService.Dtos;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Shared.Services;
using Iam.DomainService.Entities;
using Idp.DomainService.Oidc.Contracts;

namespace Authentication.DomainService.Shared
{
    public sealed class AuthSessionFacade : IAuthSessionFacade
    {
        private readonly IIdpSessionService _idpSessionService;
        private readonly IImpersonationFlowHelper _impersonationFlowHelper;
        private readonly ITokenRevocationService _tokenRevocationService;
        private readonly UnifiedTokenSessionService _unifiedTokenSessionService;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly IRefreshSessionResolver _refreshSessionResolver;

        public AuthSessionFacade(
            IIdpSessionService idpSessionService,
            IImpersonationFlowHelper impersonationFlowHelper,
            ITokenRevocationService tokenRevocationService,
            UnifiedTokenSessionService unifiedTokenSessionService,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            IRefreshSessionResolver refreshSessionResolver)
        {
            _idpSessionService = idpSessionService;
            _impersonationFlowHelper = impersonationFlowHelper;
            _tokenRevocationService = tokenRevocationService;
            _unifiedTokenSessionService = unifiedTokenSessionService;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _refreshSessionResolver = refreshSessionResolver;
        }

        public Task<string> CreateSessionAsync(string userId, string tenantId, string ipAddress)
            => _idpSessionService.CreateSessionAsync(userId, tenantId, ipAddress);

        public Task<IdpSessionModel> GetSessionAsync(string sessionId)
            => _idpSessionService.GetSessionAsync(sessionId);

        public Task<bool> AddAccountAsync(string sessionId, string userId, string tenantId, string displayName)
            => _idpSessionService.AddAccountAsync(sessionId, userId, tenantId, displayName);

        public Task<bool> UpdateActivityAsync(string sessionId)
            => _idpSessionService.UpdateActivityAsync(sessionId);

        public Task<bool> RemoveAccountAsync(string sessionId, string userId, string? tenantId = null)
            => _idpSessionService.RemoveAccountAsync(sessionId, userId, tenantId);

        public Task<string?> RotateSessionAsync(string sessionId, string reason)
            => _idpSessionService.RotateSessionAsync(sessionId, reason);

        public Task<bool> RevokeSessionAsync(string sessionId, string reason)
            => _idpSessionService.RevokeSessionAsync(sessionId, reason);

        public Task<string> CreateAndBackupImpersonationSessionAsync(string userId, string rootTenantId, string targetTenantId, string clientId, string organizationId)
            => _impersonationFlowHelper.CreateAndBackupImpersonationSessionAsync(userId, rootTenantId, targetTenantId, clientId, organizationId);

        public Task<bool> SwitchOrganizationContextAsync(string sessionId, string organizationId)
            => _impersonationFlowHelper.SwitchOrganizationContextAsync(sessionId, organizationId);

        public Task<TokenRevocationResult> RevokeTokenAsync(string token, string grantType, string? clientId)
            => _tokenRevocationService.RevokeTokenAsync(token, grantType, clientId);

        public Task RevokeRefreshToken(string refreshToken)
            => _unifiedTokenSessionService.RevokeRefreshToken(refreshToken);

        public Task<TokenResponse> ManageTokenAsync(TokenRequest tokenRequest, IdentityConfiguration authConfiguration, User? user)
            => _oAuthJwtAccessTokenManager.ManageTokenAsync(tokenRequest, authConfiguration, user);

        public Task<RefreshTokenCache?> TryResolveRefreshSessionAsync(string presentedTokenId, IdentityConfiguration configuration)
            => _refreshSessionResolver.TryResolveRefreshSessionAsync(presentedTokenId, configuration);
    }
}
