namespace Authentication.DomainService.Authentication
{
    public static class LoginAuditEvents
    {
        public const string LoginSuccess = "login_success";
        public const string LoginFailure = "login_failure";
        public const string LoginFailureAccountLocked = "login_failure_account_locked";
        public const string CaptchaValidationSuccess = "captcha_validation_success";
        public const string CaptchaValidationFailure = "captcha_validation_failure";
    }
}
