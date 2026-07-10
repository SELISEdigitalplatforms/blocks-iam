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
    /// Writes <c>OidcLoginRequest</c>-driven UserActivity events. Failures are logged but never thrown,
    /// preserving the original behavior of the inline helper in <c>AuthorizationFlowService</c>.
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

        public async Task WriteAsync(OidcLoginRequest request, User user, HttpRequest httpRequest, string eventType, string? details)
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
                    TenantId = request.TenantId ?? BlocksContext.GetContext()?.TenantId,
                    ClientId = request.ClientId,
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
                _logger.LogWarning(ex, "Failed to write OIDC login audit event {EventType} for user {UserId}", eventType, user.ItemId);
            }
        }
    }
}