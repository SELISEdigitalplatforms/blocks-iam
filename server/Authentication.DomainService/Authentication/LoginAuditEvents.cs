namespace Authentication.DomainService.Authentication
{
    public static class LoginAuditEvents
    {
        #region Login

        public const string LoginSuccess = "login_success";
        public const string LoginFailure = "login_failure";
        public const string LoginFailureAccountLocked = "login_failure_account_locked";
        public const string CaptchaValidationSuccess = "captcha_validation_success";
        public const string CaptchaValidationFailure = "captcha_validation_failure";

        #endregion

        #region OIDC Login

        public const string OidcLoginSuccess = "oidc_login_success";
        public const string OidcLoginFailure = "oidc_login_invalid_credentials";
        public const string OidcLoginCaptchaInvalid = "oidc_login_captcha_invalid";
        public const string OidcLoginAccountLocked = "oidc_login_account_locked";
        public const string OidcCallback = "oidc_callback";
        public const string CodeReuseAttack = "code_reuse_attack";
        public const string AuthorizationCodeReuseDetected = "authorization_code_reuse_detected";

        #endregion

        #region MFA

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

        #endregion

        #region Impersonation

        public const string ImpersonationStarted = "impersonation_started";
        public const string ImpersonationStopped = "impersonation_stopped";
        public const string ImpersonationStartDenied = "impersonation_start_denied";
        public const string ImpersonationStopFailed = "impersonation_stop_failed";

        #endregion
    }
}
