using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Utilities;

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

    /// <summary>
    /// How <see cref="TokenRequest.OrganizationId"/> was chosen, which decides what a disabled organization
    /// does to the issuance (spec #668 D1/D2).
    /// </summary>
    public enum OrganizationRequestSource
    {
        /// <summary>
        /// The server filled it in (the password, social and MFA sign-in pre-fill): only a preference, honoured
        /// while it is an active membership, so a disabled organization falls back to an active one.
        /// </summary>
        ServerFilled = 0,

        /// <summary>
        /// Carried in explicitly (client-sent sign-in organization, refresh hint, switch target, token-exchange
        /// grant): a disabled membership is refused, never replaced.
        /// </summary>
        Explicit,

        /// <summary>A signup-link organization: link handling as before, plus the status check.</summary>
        Link
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
        public string? OrganizationId { get; set; }

        /// <summary>How <see cref="OrganizationId"/> was chosen. A blank id is treated as none whatever this says.</summary>
        public OrganizationRequestSource OrganizationSource { get; set; }

        /// <summary>
        /// The issuance-time organization decision, set by <see cref="IJwtAccessTokenProvider.ResolveOrganizationAsync"/>
        /// before anything is issued and read by the mint, so one issuance reads organization status at most once.
        /// </summary>
        public OrganizationIssuanceDecision? OrganizationDecision { get; set; }
        public bool IsImpersonation { get; set; }
        public string? ImpersonatorUserId { get; set; }
        public string? TargetTenantId { get; set; }
        public string? OriginalTenantId { get; set; }
        public string? ImpersonationSessionId { get; set; }

        public bool IsLinkAuthentication { get; set; }
        public List<string> RestrictedRoles { get; set; } = [];
        public List<string> RestrictedPermissions { get; set; } = [];
        public List<string> Amr { get; set; } = [];
        public string? Audience { get; set; }
        public string? IdpSessionId { get; set; }

        /// <summary>
        /// The user agent of the browser that signed in, when the token is minted on a request from
        /// someone else (a server redeeming an authorization code). Used for the refresh token's device
        /// instead of the redeeming request's User-Agent.
        /// </summary>
        public string? SignInUserAgent { get; set; }

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

        /// <summary>
        /// Set when a refresh lost its rotation to an impersonation start, stop or switch: the session
        /// moved to another tenant or organization, so no token is issued and the refusal says why.
        /// </summary>
        public bool SessionChanged { get; set; }
    }
}
