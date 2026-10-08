using Authentication.DomainService.Entities;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// The single place a user's password is checked: lockout pre-check, CAPTCHA gate, BCrypt
    /// with the tenant salt, and failed-attempt accounting with lockout and its notification.
    /// Embedded login and the signup-link password step both run through it, so the two cannot
    /// drift apart. Neither verification method resets the counters: a correct password is not
    /// yet a sign-in, so each caller calls <see cref="ResetFailureCountersAsync"/> only once it
    /// has issued a session.
    /// </summary>
    public interface IPasswordCredentialVerifier
    {
        /// <summary>
        /// Full verification for a caller that has already identified the user: lockout,
        /// CAPTCHA, password and accounting. It does not reset the counters on success.
        /// </summary>
        Task<PasswordVerificationResult> VerifyAsync(
            User user,
            string? password,
            string? captchaCode,
            HttpRequest? request,
            string? tenantId);

        /// <summary>
        /// Lockout pre-check, password, and failed-attempt accounting only. Embedded login
        /// gates CAPTCHA itself, so it uses this step rather than <see cref="VerifyAsync"/>.
        /// </summary>
        Task<PasswordVerificationResult> VerifyPasswordAsync(
            User user,
            string? password,
            IdentityConfiguration configuration,
            HttpRequest? request,
            string? tenantId);

        /// <summary>Clears failed-login, failed-MFA and lockout state when any of it is set.</summary>
        Task ResetFailureCountersAsync(User user);

        bool VerifyPassword(string? password, string? passwordHash, string? optionalSalt = null);
    }

    public sealed class PasswordVerificationResult
    {
        public bool Succeeded { get; init; }
        public string? Error { get; init; }
        public string? ErrorDescription { get; init; }
        public int StatusCode { get; init; } = StatusCodes.Status200OK;
        public bool CaptchaRequired { get; init; }
        public string? CaptchaSiteKey { get; init; }

        /// <summary>This wrong attempt is the one that locked the account.</summary>
        public bool AccountLockedNow { get; init; }

        public static PasswordVerificationResult Success() => new() { Succeeded = true };
    }
}
