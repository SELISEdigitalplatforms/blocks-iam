using System.Text.Json;
using Authentication.DomainService.Services;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Entities;
using Iam.DomainService.Utilities;
using Blocks.Genesis;
using Authentication.DomainService.Dtos;

using Iam.DomainService.Entities;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Authentication;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Shared
{
    public sealed class UnifiedTokenSessionService
    {
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly ILogger<UnifiedTokenSessionService> _logger;

        public UnifiedTokenSessionService(
            ICacheClient cacheClient,
            IAuthenticationDomainService authenticationDomainService,
            IRefreshTokenRepository refreshTokenRepository,
            IUserActivityDispatcher userActivityDispatcher,
            ILogger<UnifiedTokenSessionService> logger)
        {
            _cacheClient = cacheClient;
            _authenticationDomainService = authenticationDomainService;
            _refreshTokenRepository = refreshTokenRepository;
            _userActivityDispatcher = userActivityDispatcher;
            _logger = logger;
        }

        public async Task<(string RefreshToken, DateTime ExpiresUtc)> CreateOrRotateRefreshToken(
            string? oldRefreshToken,
            RefreshTokenCache? oldRefreshTokenCache,
            TokenRequest tokenRequest,
            IdentityConfiguration authenticationConfiguration,
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
            SessionId = !string.IsNullOrWhiteSpace(tokenRequest.IdpSessionId)
                ? tokenRequest.IdpSessionId
                : tokenRequest.Request?.Cookies[IdpConstants.BuildIdpSessionCookieKey(tenant?.TenantId)],
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
                IssuedUtc = refreshTokenCache.IssuedUtc,
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
                ImpersonationId = tokenRequest.ImpersonationSessionId,
                Outcome = IdpConstants.StatusSuccess,
                ReasonCode = oldRefreshToken != null ? "rotation" : "initial_issue",
                RiskLevel = "low"
            };
            await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, addRefreshTokenEvent);

            await SendTokenActivityEventAsync(
                newRefreshToken: refreshTokenCache,
                oldRefreshTokenId: oldRefreshToken,
                tokenRequest: tokenRequest,
                tenant: tenant,
                user: user,
                impersoanted: impersoanted);

            if (!string.IsNullOrWhiteSpace(oldRefreshToken))
            {
                await _cacheClient.RemoveKeyAsync(oldRefreshToken);
                await _refreshTokenRepository.RevokeByTokenIdAsync(oldRefreshToken, "superseded_by_rotation");
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
                        ImpersonationId = tokenRequest.ImpersonationSessionId,
                        Outcome = IdpConstants.StatusSuccess,
                        ReasonCode = "superseded_by_rotation",
                        RiskLevel = "low"
                    };
                    await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, revokeOldTokenEvent);

                    await SendSupersededTokenActivityEventAsync(oldRefreshTokenCache, tokenRequest, impersoanted);
                }
            }

            return (refreshTokenId, refreshTokenExpireOn);
        }

        private async Task SendTokenActivityEventAsync(
            RefreshTokenCache newRefreshToken,
            string? oldRefreshTokenId,
            TokenRequest tokenRequest,
            Tenant tenant,
            User user,
            bool impersoanted)
        {
            try
            {
                var isRotation = !string.IsNullOrWhiteSpace(oldRefreshTokenId);
                var eventType = isRotation
                    ? LoginAuditEvents.RefreshTokenRotated
                    : LoginAuditEvents.RefreshTokenIssued;

                var userAgent = tokenRequest.Request?.Headers != null && tokenRequest.Request.Headers.ContainsKey("User-Agent")
                    ? tokenRequest.Request.Headers["User-Agent"].ToString()
                    : string.Empty;

                await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
                {
                    UserId = user.ItemId ?? string.Empty,
                    TenantId = tenant.TenantId,
                    ClientId = tokenRequest.ClientId,
                    Category = UserActivityCategory.Auth,
                    Event = eventType,
                    Source = "auth-refresh-token",
                    Severity = "low",
                    Outcome = IdpConstants.StatusSuccess,
                    SessionId = newRefreshToken.SessionId,
                    Context = new ActivityContext
                    {
                        IpAddress = newRefreshToken.IpAddresses,
                        UserAgent = userAgent,
                        DeviceInformation = _authenticationDomainService.GetDeviceInfo(userAgent)
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { "grantType", tokenRequest.GrantType ?? string.Empty },
                        { "tokenId", newRefreshToken.RefreshToken ?? string.Empty },
                        { "expiresUtc", newRefreshToken.ExpiresUtc.ToString("o") },
                        { "impersonated", impersoanted ? "true" : "false" },
                        { "supersededTokenId", oldRefreshTokenId ?? string.Empty },
                        { "reason", isRotation ? "rotation" : "initial_issue" }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish refresh-token UserActivity event for user {UserId}", user.ItemId);
            }
        }

        private async Task SendSupersededTokenActivityEventAsync(
            RefreshTokenCache oldRefreshTokenCache,
            TokenRequest tokenRequest,
            bool impersoanted)
        {
            try
            {
                var userAgent = tokenRequest.Request?.Headers != null && tokenRequest.Request.Headers.ContainsKey("User-Agent")
                    ? tokenRequest.Request.Headers["User-Agent"].ToString()
                    : string.Empty;

                await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
                {
                    UserId = oldRefreshTokenCache.UserId ?? string.Empty,
                    TenantId = oldRefreshTokenCache.TenantId,
                    ClientId = oldRefreshTokenCache.ClientId,
                    Category = UserActivityCategory.Auth,
                    Event = LoginAuditEvents.RefreshTokenSuperseded,
                    Source = "auth-refresh-token",
                    Severity = "low",
                    Outcome = IdpConstants.StatusSuccess,
                    SessionId = oldRefreshTokenCache.SessionId,
                    Context = new ActivityContext
                    {
                        IpAddress = oldRefreshTokenCache.IpAddresses,
                        UserAgent = userAgent,
                        DeviceInformation = _authenticationDomainService.GetDeviceInfo(userAgent)
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { "grantType", tokenRequest.GrantType ?? string.Empty },
                        { "tokenId", oldRefreshTokenCache.RefreshToken ?? string.Empty },
                        { "impersonated", impersoanted ? "true" : "false" },
                        { "reason", "superseded_by_rotation" }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish refresh-token superseded UserActivity event for user {UserId}", oldRefreshTokenCache.UserId);
            }
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
