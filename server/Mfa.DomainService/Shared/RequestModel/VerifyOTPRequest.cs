using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Mfa.DomainService.Shared
{
    public class VerifyOtpRequest
    {
        public string? VerificationCode { get; set; }
        public string? MfaId { get; set; }
        public UserMfaType AuthType { get; set; }
        public bool IsFromTokenCall { get; set; } = false;
    }
}
