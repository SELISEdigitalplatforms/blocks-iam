using Authentication.DomainService.OAuth;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Facade bundling MFA-related services used during the OIDC login flow.
    /// Replaces 3 separate deps (S107).
    /// </summary>
    public interface IMfaChallengeIssuer
    {
        Task<bool> IsRequiredAsync(User user);
        Task<OtpService> GetOtpServiceAsync(User user);
        Task WriteAuditAsync(MfaAuditEvent auditEvent, CancellationToken cancellationToken = default);
    }
}
