using Iam.DomainService.Entities;

namespace Authentication.DomainService.Authentication
{
    public static class CaptchaGate
    {
        public const int FailedAttemptsBeforeCaptcha = 2;

        public static bool IsCaptchaRequired(User? user) =>
            user != null && user.FailedLoginCount >= FailedAttemptsBeforeCaptcha;
    }
}
