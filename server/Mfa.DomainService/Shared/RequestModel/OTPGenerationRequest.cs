using Iam.DomainService.Entities;

namespace Mfa.DomainService.Shared
{
    public class OtpGenerationRequest
    {
        public string? UserId { get; set; }
        public UserMfaType? MfaType { get; set; }
        public string? SendPhoneNumberAsEmailDomain { get; set; }
    }
}
