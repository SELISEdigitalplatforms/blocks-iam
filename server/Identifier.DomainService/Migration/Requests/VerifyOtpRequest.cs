using Blocks.Genesis;

namespace DomainService.Migration
{
    public class VerifyOtpRequest
    {
        public string VerificationId { get; set; }
        public string VerificationCode { get; set; }
    }
    public class OtpVerificationResponse : BaseResponse
    {
        public bool IsValid { get; set; }
    }
}
