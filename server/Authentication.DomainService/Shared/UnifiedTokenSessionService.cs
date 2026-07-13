using System.Text.Json;
using Authentication.DomainService.Services;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Services;
using Iam.DomainService.Utilities;
using Blocks.Genesis;
using Authentication.DomainService.Dtos;

using Iam.DomainService.Entities;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Shared
{
    public sealed class UnifiedTokenSessionService
    {
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly IIdpSessionService _idpSessionService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IUserRepository _userRepository;
        private readonly ILogger<UnifiedTokenSessionService> _logger;

        public UnifiedTokenSessionService(
            ICacheClient cacheClient,
            IAuthenticationDomainService authenticationDomainService,
            IRefreshTokenRepository refreshTokenRepository,
            IUserActivityDispatcher userActivityDispatcher,
            IIdpSessionService idpSessionService,
            IHttpContextAccessor httpContextAccessor,
            IUserRepository userRepository,
            ILogger<UnifiedTokenSessionService> logger)
        {
            _cacheClient = cacheClient;
            _authenticationDomainService = authenticationDomainService;
            _refreshTokenRepository = refreshTokenRepository;
            _userActivityDispatcher = userActivityDispatcher;
            _idpSessionService = idpSessionService;
            _httpContextAccessor = httpContextAccessor;
            _userRepository = userRepository;
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

            var httpContext = _httpContextAccessor.HttpContext;
            var userAgent = tokenRequest.Request?.Headers != null && tokenRequest.Request.Headers.ContainsKey("User-Agent")
                ? tokenRequest.Request.Headers["User-Agent"].ToString()
                : string.Empty;
            var deviceInformation = _authenticationDomainService.GetDeviceInfo(userAgent);
            var ipAddress = string.Join(",", visitorsIpAddresses);

            // Resolve/create IdP session. The cookie is written by the helper when a new id is minted.
            var explicitSessionId = !string.IsNullOrWhiteSpace(tokenRequest.IdpSessionId) ? tokenRequest.IdpSessionId : null;
            string sessionId;
            if (!string.IsNullOrWhiteSpace(explicitSessionId))
            {
                sessionId = explicitSessionId!;
            }
            else
            {
                sessionId = await _idpSessionService.ResolveOrCreateAsync(
                    httpContext,
                    user.ItemId ?? string.Empty,
                    tenant.TenantId,
                    httpContext?.Connection?.RemoteIpAddress?.ToString());
            }

            var refreshTokenCache = new RefreshTokenCache
            {
                RefreshToken = refreshTokenId,
                TenantId = tenant.TenantId,
                OrganizationId = tokenRequest.OrganizationId,
                ClientId = tokenRequest.ClientId,
                SessionId = sessionId,
                IssuedUtc = now,
                ExpiresUtc = refreshTokenExpireOn,
                AbsoluteExpiresUtc = absoluteRefreshTokenExpireOn,
                IpAddresses = ipAddress,
                UserId = user.ItemId ?? string.Empty,
                RememberMe = tokenRequest.RememberMe,
                TokenVersion = user.TokenVersion,
                RememberMeIssuedUtc = tokenRequest.RememberMe ? now : null,
                RememberMeExpiresUtc = tokenRequest.RememberMe ? absoluteRefreshTokenExpireOn : null,
                Scope = tokenRequest.Scope,
                Impersonated = impersoanted,
                ImpersonationId = tokenRequest.ImpersonationSessionId
            };

            // Persist to Redis cache.
            await _cacheClient.AddStringValueAsync(refreshTokenCache.RefreshToken!, JsonSerializer.Serialize(refreshTokenCache), refreshTokenLifetime * 60);

            // Persist to MongoDB. Validation in CreateAsync rejects empty TokenId/UserId/TenantId/ClientId/SessionId.
            var refreshTokenModel = new Idp.DomainService.Oidc.Contracts.RefreshTokenModel
            {
                TokenId = refreshTokenCache.RefreshToken!,
                UserId = refreshTokenCache.UserId!,
                TenantId = refreshTokenCache.TenantId!,
                OrganizationId = refreshTokenCache.OrganizationId,
                ClientId = refreshTokenCache.ClientId!,
                SessionId = refreshTokenCache.SessionId,
                Scope = refreshTokenCache.Scope,
                GrantType = tokenRequest.GrantType,
                DeviceInformation = deviceInformation,
                SlidingExpiry = refreshTokenCache.ExpiresUtc,
                AbsoluteExpiry = refreshTokenCache.AbsoluteExpiresUtc,
                IssuedUtc = refreshTokenCache.IssuedUtc,
                IpAddress = refreshTokenCache.IpAddresses,
                IsRevoked = false,
                Impersonated = impersoanted,
                ImpersonationId = tokenRequest.ImpersonationSessionId,
                UserAgent = userAgent
            };
            await _refreshTokenRepository.CreateAsync(refreshTokenModel);

            // First-issue user-login counters update (replaces the old RefreshTokenWorkerService async path).
            if (string.IsNullOrWhiteSpace(oldRefreshToken))
            {
                await UpdateUserByLoginInfoAsync(user.ItemId ?? string.Empty, deviceInformation);
            }

            await SendTokenActivityEventAsync(
                newRefreshToken: refreshTokenCache,
                oldRefreshTokenId: oldRefreshToken,
                tokenRequest: tokenRequest,
                tenant: tenant,
                user: user,
                impersoanted: impersoanted);

            if (!string.IsNullOrWhiteSpace(oldRefreshToken))
            {
                await _cacheClient.RemoveKeyAsync(oldRefreshToken!);
                await _refreshTokenRepository.RevokeByTokenIdAsync(oldRefreshToken!, "superseded_by_rotation");
                if (oldRefreshTokenCache != null)
                {
                    await SendSupersededTokenActivityEventAsync(oldRefreshTokenCache, tokenRequest, impersoanted);
                }
            }

            return (refreshTokenId, refreshTokenExpireOn);
        }

        private async Task UpdateUserByLoginInfoAsync(string userId, DeviceInformation deviceInformation)
        {
            try
            {
                var existing = await _userRepository.GetUserByIdAsync(userId);
                if (existing == null)
                {
                    _logger.LogError("User not found by this user id: {Id}", userId);
                    return;
                }

                if (existing.LogInCount == 0)
                {
                    existing.FirstLoggedInTime = DateTime.UtcNow;
                }

                existing.LogInCount += 1;
                existing.LastLoggedInTime = DateTime.UtcNow;
                existing.LastLoggedInDeviceInfo = JsonSerializer.Serialize(deviceInformation);

                await _userRepository.UpdateUserAsync(existing);

                _logger.LogInformation("User login info updated for {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update user login info for {UserId}", userId);
            }
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
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return;
            }

            await _cacheClient.RemoveKeyAsync(refreshToken);
            await _refreshTokenRepository.DeleteAsync(refreshToken);
        }
    }
}
