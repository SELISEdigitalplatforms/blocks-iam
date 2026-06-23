namespace Authentication.DomainService.Authentication
{
    public static class LoginAuditEvents
    {
        public const string LoginSuccess = "login_success";
        public const string LoginFailure = "login_failure";
        public const string LoginFailureAccountLocked = "login_failure_account_locked";
        public const string CaptchaValidationSuccess = "captcha_validation_success";
        public const string CaptchaValidationFailure = "captcha_validation_failure";

        public const string MfaEnabled = "mfa_enabled";
        public const string MfaDisabled = "mfa_disabled";
        public const string MfaEnrollmentCompleted = "mfa_enrollment_completed";
        public const string MfaEnrollmentFailed = "mfa_enrollment_failed";
        public const string MfaVerificationSuccess = "mfa_verification_success";
        public const string MfaVerificationFailure = "mfa_verification_failure";
        public const string MfaReset = "mfa_reset";
        public const string MfaMethodChanged = "mfa_method_changed";
        public const string MfaAccountLocked = "mfa_account_locked";
        public const string MfaBackupCodesGenerated = "mfa_backup_codes_generated";
        public const string MfaBackupCodeUsed = "mfa_backup_code_used";
        public const string MfaPolicyUpdated = "mfa_policy_updated";
    }
}
