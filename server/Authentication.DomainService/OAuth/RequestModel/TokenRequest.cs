using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;

namespace Authentication.DomainService.OAuth.RequestModel
{
    /// <summary>
    /// The signed presentation of a delegation grant. Every field is caller-supplied and none of
    /// it is trusted until the signature verifies against the tenant salt.
    /// </summary>
    public sealed class TokenExchangeRequest
    {
        public string? SubjectToken { get; init; }
        public string? SubjectTokenType { get; init; }
        public string? Nonce { get; init; }
        public string? Ts { get; init; }
        public string? Signature { get; init; }
    }

    public sealed class TokenRequest
    {
        public string? GrantType { get; set; }
        public string? Code { get; set; }
        public string? MfaId { get; set; }
        public UserMfaType MfaType { get; set; }
        public string? RedirectUri { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Scope { get; set; }
        public bool RememberMe { get; set; }
        public HttpRequest? Request { get; set; }
        public string? RefreshToken { get; set; }
        public string? State { get; set; }
        public string? Language { get; set; }
        public string? BiometricId { get; set; }
        public string? BiometricKey { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? UserCode { get; set; }
        public string? OrganizationId { get; set; }
        public bool IsImpersonation { get; set; }
        public string? ImpersonatorUserId { get; set; }
        public string? TargetTenantId { get; set; }
        public string? OriginalTenantId { get; set; }
        public string? ImpersonationSessionId { get; set; }
        public string? IdpSessionId { get; set; }

        /// <summary>
        /// RFC 8693 parameters, set only for <see cref="OAuth.GrantTypes.TokenExchange"/>.
        /// </summary>
        public TokenExchangeRequest? TokenExchange { get; set; }

        /// <summary>
        /// Set when the presented refresh token was superseded by rotation inside the grace window and
        /// has been resolved to the successor named here. The successor is returned as-is: a retry is a
        /// replay of an issuance that already happened, so it must not consume another rotation or
        /// advance any clock.
        /// </summary>
        public string? GraceReplayTokenId { get; set; }

        /// <summary>
        /// The absolute expiry of <see cref="GraceReplayTokenId"/>, so a replay can write the same cookie
        /// the original issuance did.
        /// </summary>
        public DateTime? GraceReplayAbsoluteExpiry { get; set; }
    }
}
