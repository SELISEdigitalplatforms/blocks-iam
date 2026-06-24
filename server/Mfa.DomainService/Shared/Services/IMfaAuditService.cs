using Iam.DomainService.Entities;

namespace Mfa.DomainService.Services
{
    public interface IMfaAuditService
    {
        Task WriteAsync(MfaAuditEvent auditEvent, CancellationToken cancellationToken = default);
    }

    public class MfaAuditEvent
    {
        public string EventType { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? ClientId { get; set; }
        public string? TenantId { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public UserMfaType? MfaType { get; set; }
        public string Severity { get; set; } = "INFO";
        public string Status { get; set; } = "success";
        public string? Details { get; set; }
        public string? ActorUserId { get; set; }
    }
}
