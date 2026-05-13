using Blocks.Genesis;

namespace Identifier.DomainService.Migration
{
    public class MigrationVerifyOtpRequest
    {
        public string VerificationId { get; set; }
        public string VerificationCode { get; set; }
    }
    public class MigrationOtpVerificationResponse : BaseResponse
    {
        public bool IsValid { get; set; }
    }
}
