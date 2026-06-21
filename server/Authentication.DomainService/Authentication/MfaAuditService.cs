using Authentication.DomainService.Oidc.Repositories;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Mfa.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Authentication
{
    public class MfaAuditService : IMfaAuditService
    {
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<MfaAuditService> _logger;

        public MfaAuditService(
            IAuditLogRepository auditLogRepository,
            IHttpContextAccessor httpContextAccessor,
            ILogger<MfaAuditService> logger)
        {
            _auditLogRepository = auditLogRepository;
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
                    IpAddress = auditEvent.IpAddress ?? GetClientIp(http),
                    UserAgent = auditEvent.UserAgent ?? (http?.Request?.Headers?.UserAgent.ToString() ?? string.Empty),
                    Severity = auditEvent.Severity,
                    Status = auditEvent.Status,
                    Details = auditEvent.Details ?? auditEvent.EventType,
                    Timestamp = DateTime.UtcNow,
                    Message = auditEvent.MfaType.HasValue ? auditEvent.MfaType.Value.ToString() : null
                };

                await _auditLogRepository.CreateAsync(log);
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

        private static string GetClientIp(HttpContext? context) => GetClientIpAddress(context);
    }
}
