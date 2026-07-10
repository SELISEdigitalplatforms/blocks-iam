using Authentication.DomainService.Dtos;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Iam.DomainService.Utilities;
using Mfa.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Authentication
{
    public sealed class MfaAuditService : IMfaAuditService
    {
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<MfaAuditService> _logger;

        public MfaAuditService(
            IAuditLogRepository auditLogRepository,
            IAuthenticationDomainService authenticationDomainService,
            IHttpContextAccessor httpContextAccessor,
            ILogger<MfaAuditService> logger)
        {
            _auditLogRepository = auditLogRepository;
            _authenticationDomainService = authenticationDomainService;
            _httpContextAccessor = httpContextAccessor;
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

                var log = new AuditLogModel
                {
                    EventType = auditEvent.EventType,
                    UserId = auditEvent.UserId,
                    ClientId = auditEvent.ClientId,
                    TenantId = tenantId,
                    IpAddress = auditEvent.IpAddress ?? GetClientIpAddress(http),
                    UserAgent = auditEvent.UserAgent ?? (http?.Request?.Headers?.UserAgent.ToString() ?? string.Empty),
                    Severity = auditEvent.Severity,
                    Status = auditEvent.Status,
                    Details = auditEvent.Details ?? auditEvent.EventType,
                    Timestamp = DateTime.UtcNow,
                    Message = auditEvent.MfaType.HasValue ? auditEvent.MfaType.Value.ToString() : null
                };

                await _auditLogRepository.CreateAsync(log);

                var timelineEvent = new UserAuthenticationTimelineEvent
                {
                    UserId = auditEvent.UserId,
                    Event = auditEvent.EventType,
                    ActionBy = "MfaAuditService",
                    TenantId = tenantId,
                    ClientId = auditEvent.ClientId,
                    Outcome = auditEvent.Status,
                    DeviceInformation = _authenticationDomainService.GetDeviceInfo(auditEvent.UserAgent ?? (http?.Request?.Headers?.UserAgent.ToString() ?? string.Empty)),
                    IpAddresses = auditEvent.IpAddress ?? GetClientIpAddress(http),
                    ReasonCode = auditEvent.Details,
                    RiskLevel = auditEvent.Status == IdpConstants.StatusFailure ? "high" : "low"
                };

                await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, timelineEvent);
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
