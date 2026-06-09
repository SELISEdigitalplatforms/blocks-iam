using System.Text.Json;
using Authentication.DomainService.Services;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Authentication.DomainService.Dtos;

using Iam.DomainService.Entities;
using Iam.DomainService.Dtos;
using Authentication.DomainService.Oidc.Repositories;

namespace Authentication.DomainService.Shared
{
    public class UnifiedTokenSessionService
    {
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IRefreshTokenRepository _refreshTokenRepository;

        public UnifiedTokenSessionService(
            ICacheClient cacheClient,
            IAuthenticationDomainService authenticationDomainService,
            IRefreshTokenRepository refreshTokenRepository)
        {
            _cacheClient = cacheClient;
            _authenticationDomainService = authenticationDomainService;
            _refreshTokenRepository = refreshTokenRepository;
        }

        public async Task<(string RefreshToken, DateTime ExpiresUtc)> CreateOrRotateRefreshToken(
            string? oldRefreshToken,
            RefreshTokenCache? oldRefreshTokenCache,
            TokenRequest tokenRequest,
            AuthenticationConfiguration authenticationConfiguration,
            Tenant tenant,
            User user,
            IEnumerable<string> visitorsIpAddresses,
            bool impersoanted)
        {
            var now = DateTime.UtcNow;
            string refreshTokenId = Guid.NewGuid().ToString("N");
            int refreshTokenLifetime = authenticationConfiguration.RefreshTokenValidForNumberMinutes > 0 ? authenticationConfiguration.RefreshTokenValidForNumberMinutes : 15;
            int absoluteLifetime = authenticationConfiguration.AbsoluteRefreshTokenValidForNumberMinutes > 0 ? authenticationConfiguration.AbsoluteRefreshTokenValidForNumberMinutes : refreshTokenLifetime;
            if (absoluteLifetime < refreshTokenLifetime) absoluteLifetime = refreshTokenLifetime;
            DateTime refreshTokenExpireOn = now.AddMinutes(refreshTokenLifetime);
            DateTime absoluteRefreshTokenExpireOn = now.AddMinutes(absoluteLifetime);

            var refreshTokenCache = new RefreshTokenCache
            {
                RefreshToken = refreshTokenId,
                TenantId = tenant.TenantId,
                OrganizationId = tokenRequest.OrganizationId,
                ClientId = tokenRequest.ClientId,
                SessionId = tokenRequest.Request?.Cookies["idp_session_id"],
                IssuedUtc = now,
                ExpiresUtc = refreshTokenExpireOn,
                AbsoluteExpiresUtc = absoluteRefreshTokenExpireOn,
                IpAddresses = string.Join(",", visitorsIpAddresses),
                UserId = user.ItemId ?? string.Empty,
                RememberMe = tokenRequest.RememberMe,
                TokenVersion = user.TokenVersion,
                RememberMeIssuedUtc = tokenRequest.RememberMe ? now : null,
                RememberMeExpiresUtc = tokenRequest.RememberMe ? absoluteRefreshTokenExpireOn : null,
                Scope = tokenRequest.Scope,
                Impersonated = impersoanted,
                ImpersonationId = tokenRequest.ImpersonationSessionId
            };


            // Persist to cache
            await _cacheClient.AddStringValueAsync(refreshTokenCache.RefreshToken, JsonSerializer.Serialize(refreshTokenCache), refreshTokenLifetime * 60);

            // Persist to MongoDB
            var refreshTokenModel = new Idp.DomainService.Oidc.Contracts.RefreshTokenModel
            {
                TokenId = refreshTokenCache.RefreshToken ?? string.Empty,
                UserId = refreshTokenCache.UserId ?? string.Empty,
                TenantId = refreshTokenCache.TenantId ?? string.Empty,
                OrgId = refreshTokenCache.OrganizationId ?? string.Empty,
                ClientId = refreshTokenCache.ClientId ?? string.Empty,
                SessionId = refreshTokenCache.SessionId ?? string.Empty,
                Scope = refreshTokenCache.Scope ?? string.Empty,
                SlidingExpiry = refreshTokenCache.ExpiresUtc,
                AbsoluteExpiry = refreshTokenCache.AbsoluteExpiresUtc,
                IpAddress = refreshTokenCache.IpAddresses ?? string.Empty,
                IsRevoked = false,
                Impersonated = impersoanted,
                ImpersonationId = tokenRequest.ImpersonationSessionId,
                UserAgent = tokenRequest.Request?.Headers != null && tokenRequest.Request.Headers.ContainsKey("User-Agent") ? tokenRequest.Request.Headers["User-Agent"].ToString() : string.Empty
            };
            await _refreshTokenRepository.CreateAsync(refreshTokenModel);

            var addRefreshTokenEvent = new RefreshTokenEvent
            {
                RefreshToken = refreshTokenCache.RefreshToken ?? string.Empty,
                TenantId = refreshTokenCache.TenantId ?? string.Empty,
                OrganizationId = refreshTokenCache.OrganizationId ?? string.Empty,
                ClientId = refreshTokenCache.ClientId ?? string.Empty,
                SessionId = refreshTokenCache.SessionId ?? string.Empty,
                IssuedUtc = refreshTokenCache.IssuedUtc,
                ExpiresUtc = refreshTokenCache.ExpiresUtc,
                IpAddresses = refreshTokenCache.IpAddresses ?? string.Empty,
                UserId = refreshTokenCache.UserId ?? string.Empty,
                DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers != null && tokenRequest.Request.Headers.ContainsKey("User-Agent") ? tokenRequest.Request.Headers["User-Agent"].ToString() : string.Empty),
                IsRevoke = false,
                IsLogin = true,
                GrantType = tokenRequest.GrantType ?? string.Empty,
                Impersonated = impersoanted,
                ImpersonationId = tokenRequest.ImpersonationSessionId
            };
            await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, addRefreshTokenEvent);


            if (!string.IsNullOrWhiteSpace(oldRefreshToken))
            {
                await _cacheClient.RemoveKeyAsync(oldRefreshToken);
                await _refreshTokenRepository.DeleteAsync(oldRefreshToken);
                if (oldRefreshTokenCache != null)
                {
                    var revokeOldTokenEvent = new RefreshTokenEvent
                    {
                        RefreshToken = oldRefreshToken ?? string.Empty,
                        TenantId = oldRefreshTokenCache.TenantId ?? string.Empty,
                        OrganizationId = oldRefreshTokenCache.OrganizationId ?? string.Empty,
                        ClientId = oldRefreshTokenCache.ClientId ?? string.Empty,
                        SessionId = oldRefreshTokenCache.SessionId ?? string.Empty,
                        IssuedUtc = oldRefreshTokenCache.IssuedUtc,
                        ExpiresUtc = oldRefreshTokenCache.ExpiresUtc,
                        IpAddresses = oldRefreshTokenCache.IpAddresses ?? string.Empty,
                        UserId = oldRefreshTokenCache.UserId ?? string.Empty,
                        DeviceInformation = _authenticationDomainService.GetDeviceInfo(tokenRequest.Request?.Headers != null && tokenRequest.Request.Headers.ContainsKey("User-Agent") ? tokenRequest.Request.Headers["User-Agent"].ToString() : string.Empty),
                        IsRevoke = true,
                        IsLogin = false,
                        GrantType = tokenRequest.GrantType ?? string.Empty,
                        Impersonated = impersoanted,
                        ImpersonationId = tokenRequest.ImpersonationSessionId
                    };
                    await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, revokeOldTokenEvent);
                }
            }

            return (refreshTokenId, refreshTokenExpireOn);
        }

        public async Task RevokeRefreshToken(string refreshToken)
        {
            // Remove from cache
            await _cacheClient.RemoveKeyAsync(refreshToken);
            // Remove from MongoDB
            await _refreshTokenRepository.DeleteAsync(refreshToken);
        }
    }
}
