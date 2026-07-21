using Iam.DomainService.Utilities;
using Authentication.DomainService.Services;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Blocks.Genesis;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Writes <c>UserActivity</c> events for any login flow (OIDC headless, embedded,
    /// social). Failures are logged at <c>Error</c> level — never silently dropped — so
    /// dropped events are visible in monitoring instead of disappearing into the void.
    /// </summary>
    public sealed class OidcLoginAuditWriter
    {
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly ILogger<OidcLoginAuditWriter> _logger;

        public OidcLoginAuditWriter(
            IAuthenticationDomainService authenticationDomainService,
            IUserActivityDispatcher userActivityDispatcher,
            ILogger<OidcLoginAuditWriter> logger)
        {
            _authenticationDomainService = authenticationDomainService;
            _userActivityDispatcher = userActivityDispatcher;
            _logger = logger;
        }

        public Task WriteAsync(OidcLoginRequest request, User user, HttpRequest httpRequest, string eventType, string? details)
        {
            return WriteAsync(request.TenantId, request.ClientId, user, httpRequest, eventType, details);
        }

        public async Task WriteAsync(string? tenantId, string? clientId, User user, HttpRequest httpRequest, string eventType, string? details)
        {
            try
            {
                var isFailure = eventType.Contains("failure", StringComparison.OrdinalIgnoreCase)
                    || eventType.Contains("locked", StringComparison.OrdinalIgnoreCase);
                var isSuccess = eventType.Contains("success", StringComparison.OrdinalIgnoreCase);
                var ipAddress = OidcRedirectUrlBuilder.GetClientIpAddress(httpRequest);
                var userAgent = httpRequest?.Headers?.UserAgent.ToString() ?? string.Empty;

                await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
                {
                    UserId = user.ItemId,
                    Category = UserActivityCategory.Auth,
                    Event = eventType,
                    Source = "auth-oidc-login",
                    Severity = isFailure ? "medium" : "low",
                    Outcome = isSuccess ? IdpConstants.StatusSuccess : IdpConstants.StatusFailure,
                    ReasonCode = isFailure ? eventType : null,
                    TenantId = tenantId ?? BlocksContext.GetContext()?.TenantId,
                    ClientId = clientId,
                    Context = new ActivityContext
                    {
                        IpAddress = ipAddress,
                        UserAgent = userAgent,
                        DeviceInformation = _authenticationDomainService.GetDeviceInfo(userAgent)
                    },
                    Metadata = details is null ? null : new Dictionary<string, string> { { "details", details } }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to publish UserActivity event {EventType} for user {UserId} (message lost — activity will be missing from history)",
                    eventType, user?.ItemId);
            }
        }
    }
}
