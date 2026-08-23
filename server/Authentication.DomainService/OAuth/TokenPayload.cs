using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.DomainService.OAuth
{
    public sealed class TokenPayload
    {
        [FromForm(Name = "grant_type")]
        public string GrantType { get; set; }

        [FromForm(Name = "code")]
        public string Code { get; set; } = string.Empty;

        [FromForm(Name = "redirect_uri")]
        public string RedirectUri { get; set; } = string.Empty;

        [FromForm(Name = "username")]
        public string Username { get; set; } = string.Empty;

        [FromForm(Name = "password")]
        public string Password { get; set; } = string.Empty;

        [FromForm(Name = "scope")]
        public string Scope { get; set; } = string.Empty;

        [FromForm(Name = "remember_me")]
        public bool RememberMe { get; set; }

        [FromForm(Name = "refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [FromForm(Name = "mfa_id")]
        public string MfaId { get; set; } = string.Empty;

        [FromForm(Name = "mfa_type")]
        public UserMfaType MfaType { get; set; }

        [FromForm(Name = "state")]
        public string State { get; set; } = string.Empty;

        [FromForm(Name = "language")]
        public string Language { get; set; } = string.Empty;

        [FromForm(Name = "biometric_id")]
        public string BiometricId { get; set; } = string.Empty;

        [FromForm(Name = "biometric_key")]
        public string BiometricKey { get; set; } = string.Empty;

        [FromForm(Name = "client_id")]
        public string ClientId { get; set; } = string.Empty;

        [FromForm(Name = "client_secret")]
        public string ClientSecret { get; set; } = string.Empty;
        [FromForm(Name = "user_code")]
        public string UserSecret { get; set; } = string.Empty;

        [FromForm(Name = "org_id")]
        public string OrganizationId { get; set; } = string.Empty;

        #region RFC 8693 token exchange (delegated access)

        /// <summary>The opaque delegation grant id: <c>dg_</c> plus 64 lowercase hex chars.</summary>
        [FromForm(Name = "subject_token")]
        public string SubjectToken { get; set; } = string.Empty;

        [FromForm(Name = "subject_token_type")]
        public string SubjectTokenType { get; set; } = string.Empty;

        /// <summary>Single-use nonce, hex. Guards against replay of a captured exchange.</summary>
        [FromForm(Name = "nonce")]
        public string Nonce { get; set; } = string.Empty;

        /// <summary>Unix seconds. Must be inside the accepted clock window.</summary>
        [FromForm(Name = "ts")]
        public string Ts { get; set; } = string.Empty;

        /// <summary>HMAC-SHA256 over <c>{tenantId}|{id}|{nonce}|{ts}</c>, keyed by the tenant salt.</summary>
        [FromForm(Name = "sig")]
        public string Signature { get; set; } = string.Empty;

        #endregion
    }
}
