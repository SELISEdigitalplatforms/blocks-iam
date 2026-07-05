using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Iam.DomainService.Entities;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;

namespace Authentication.DomainService.Authentication
{
    public sealed class MfaChallengeIssuer : IMfaChallengeIssuer
    {
        private readonly IMfaPolicyService _mfaPolicyService;
        private readonly IMfaAuditService _mfaAuditService;
        private readonly IOtpServiceFactory _otpServiceFactory;

        public MfaChallengeIssuer(
            IMfaPolicyService mfaPolicyService,
            IMfaAuditService mfaAuditService,
            IOtpServiceFactory otpServiceFactory)
        {
            _mfaPolicyService = mfaPolicyService;
            _mfaAuditService = mfaAuditService;
            _otpServiceFactory = otpServiceFactory;
        }

        public async Task<bool> IsRequiredAsync(User user)
        {
            var decision = await _mfaPolicyService.EvaluateAsync(user, clientId: null);
            return decision.Required;
        }

        public Task<IOtpService> GetOtpServiceAsync(User user)
        {
            return Task.FromResult(_otpServiceFactory.GetOTPService(user.UserMfaType));
        }

        public Task WriteAuditAsync(MfaAuditEvent auditEvent, CancellationToken cancellationToken = default)
            => _mfaAuditService.WriteAsync(auditEvent, cancellationToken);
    }
}
