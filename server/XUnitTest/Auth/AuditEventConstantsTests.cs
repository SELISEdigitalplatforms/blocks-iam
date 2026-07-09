using Authentication.DomainService.Authentication;
using FluentAssertions;

namespace XUnitTest.Auth
{
    public class AuditEventConstantsTests
    {
        [Theory]
        [InlineData(LoginAuditEvents.LoginSuccess, "login_success")]
        [InlineData(LoginAuditEvents.LoginFailure, "login_failure")]
        [InlineData(LoginAuditEvents.LoginFailureAccountLocked, "login_failure_account_locked")]
        [InlineData(LoginAuditEvents.CaptchaValidationSuccess, "captcha_validation_success")]
        [InlineData(LoginAuditEvents.CaptchaValidationFailure, "captcha_validation_failure")]
        [InlineData(LoginAuditEvents.OidcLoginSuccess, "oidc_login_success")]
        [InlineData(LoginAuditEvents.OidcLoginFailure, "oidc_login_invalid_credentials")]
        [InlineData(LoginAuditEvents.OidcLoginCaptchaInvalid, "oidc_login_captcha_invalid")]
        [InlineData(LoginAuditEvents.OidcLoginAccountLocked, "oidc_login_account_locked")]
        [InlineData(LoginAuditEvents.MfaEnabled, "mfa_enabled")]
        [InlineData(LoginAuditEvents.MfaDisabled, "mfa_disabled")]
        [InlineData(LoginAuditEvents.MfaVerificationSuccess, "mfa_verification_success")]
        [InlineData(LoginAuditEvents.MfaVerificationFailure, "mfa_verification_failure")]
        [InlineData(LoginAuditEvents.ImpersonationStarted, "impersonation_started")]
        [InlineData(LoginAuditEvents.ImpersonationStopped, "impersonation_stopped")]
        public void LoginAuditEvents_HaveExpectedValues(string actual, string expected)
        {
            actual.Should().Be(expected);
        }

        [Theory]
        [InlineData(SessionAuditEvents.SessionCreated, "session_created")]
        [InlineData(SessionAuditEvents.AccountAdded, "account_added")]
        [InlineData(SessionAuditEvents.AccountSelected, "account_selected")]
        [InlineData(SessionAuditEvents.AccountRemoved, "account_removed")]
        [InlineData(SessionAuditEvents.SessionRotated, "session_rotated")]
        [InlineData(SessionAuditEvents.SessionRevoked, "session_revoked")]
        public void SessionAuditEvents_HaveExpectedValues(string actual, string expected)
        {
            actual.Should().Be(expected);
        }

        [Theory]
        [InlineData(BackchannelAuditEvents.Dispatch, "dispatch_backchannel_logout")]
        [InlineData(BackchannelAuditEvents.Delivery, "backchannel_logout_delivery")]
        [InlineData(BackchannelAuditEvents.Delivered, "backchannel_logout_delivered")]
        [InlineData(BackchannelAuditEvents.DeliveryFailed, "backchannel_logout_delivery_failed")]
        [InlineData(BackchannelAuditEvents.Succeeded, "backchannel_logout_succeeded")]
        [InlineData(BackchannelAuditEvents.Failed, "backchannel_logout_failed")]
        [InlineData(BackchannelAuditEvents.Exception, "backchannel_logout_exception")]
        public void BackchannelAuditEvents_HaveExpectedValues(string actual, string expected)
        {
            actual.Should().Be(expected);
        }

        [Theory]
        [InlineData(MfaPolicyReasons.NoUser, "no_user")]
        [InlineData(MfaPolicyReasons.MfaDisabledGlobally, "mfa_disabled_globally")]
        [InlineData(MfaPolicyReasons.RoleExempt, "role_exempt")]
        [InlineData(MfaPolicyReasons.NoPolicyMatch, "no_policy_match")]
        [InlineData(MfaPolicyReasons.GlobalPolicy, "global_policy")]
        [InlineData(MfaPolicyReasons.RolePolicy, "role_policy")]
        [InlineData(MfaPolicyReasons.ClientPolicy, "client_policy")]
        [InlineData(MfaPolicyReasons.UserEnrolled, "user_enrolled")]
        public void MfaPolicyReasons_HaveExpectedValues(string actual, string expected)
        {
            actual.Should().Be(expected);
        }
    }
}