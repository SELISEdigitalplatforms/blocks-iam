using Blocks.Genesis;

namespace Blocks.CaptchaDriver
{
    public class VerifyCaptchaRequest
    {
        public string VerificationCode { get; set; }
        public string ConfigurationName { get; set; }
    }

    public class VerifyCaptchaRequestResponse : BaseMutationResponse
    {
        public VerifyCaptchaRequestResponse()
        {
            Verified = false;
            HostName = "";
        }

        public bool Verified { get; set; }
        public string HostName { get; set; }
    }
}