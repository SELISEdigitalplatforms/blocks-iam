using Authentication.DomainService.Dtos;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Mfa.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Authentication
{
    public sealed class MfaAuditService : IMfaAuditService
    {
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IUserActivityDispatcher _userActivityDispatcher;
        private readonly ILogger<MfaAuditService> _logger;

        public MfaAuditService(
            IAuthenticationDomainService authenticationDomainService,
            IHttpContextAccessor httpContextAccessor,
            IUserActivityDispatcher userActivityDispatcher,
            ILogger<MfaAuditService> logger)
        {
            _authenticationDomainService = authenticationDomainService;
            _httpContextAccessor = httpContextAccessor;
            _userActivityDispatcher = userActivityDispatcher;
            _logger = logger;
        }

        public async Task WriteAsync(MfaAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            try
            {
                var http = _httpContextAccessor.HttpContext;
                var tenantId = auditEvent.TenantId
                    ?? BlocksContext.GetContext()?.TenantId
                    ?? string.Empty;
                var userAgent = auditEvent.UserAgent ?? (http?.Request?.Headers?.UserAgent.ToString() ?? string.Empty);
                var ipAddress = auditEvent.IpAddress ?? GetClientIpAddress(http);

                await _userActivityDispatcher.SendUserActivityAsync(new UserActivityEvent
                {
                    UserId = auditEvent.UserId ?? string.Empty,
                    Category = UserActivityCategory.Audit,
                    Event = auditEvent.EventType,
                    Source = "auth-mfa",
                    Severity = auditEvent.Status == IdpConstants.StatusFailure ? "high" : "low",
                    Outcome = auditEvent.Status,
                    ReasonCode = auditEvent.Details,
                    TenantId = tenantId,
                    ClientId = auditEvent.ClientId,
                    Context = new ActivityContext
                    {
                        IpAddress = ipAddress,
                        UserAgent = userAgent,
                        DeviceInformation = _authenticationDomainService.GetDeviceInfo(userAgent)
                    },
                    Metadata = auditEvent.MfaType.HasValue
                        ? new Dictionary<string, string> { { "mfaType", auditEvent.MfaType.Value.ToString() } }
                        : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write MFA audit event {EventType} for user {UserId}", auditEvent.EventType, auditEvent.UserId);
            }
        }

        private static string GetClientIpAddress(HttpContext? context)
        {
            if (context?.Connection?.RemoteIpAddress != null)
            {
                return context.Connection.RemoteIpAddress.ToString();
            }
            return "unknown";
        }
    }
}