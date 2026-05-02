using Blocks.Genesis.Auth;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DomainService.Oidc.Repositories
{
    /// <summary>
    /// Token Revocation Service Interface
    /// Supports RFC 7009 (Token Revocation) and RFC 7662 (Token Introspection)
    /// </summary>
    public interface ITokenRevocationService
    {
        /// <summary>
        /// RFC 7009: Revoke a token
        /// </summary>
        Task<TokenRevocationResult> RevokeTokenAsync(string token, string tokenTypeHint, string clientId);

        /// <summary>
        /// RFC 7662: Introspect a token to get active status and claims
        /// </summary>
        Task<TokenIntrospectionResult> IntrospectTokenAsync(string token, string tokenTypeHint, string clientId);

        /// <summary>
        /// Revoke all tokens for a user (on logout, password change, etc)
        /// </summary>
        Task<bool> RevokeAllUserTokensAsync(string userId, string tenantId, string reason);

        /// <summary>
        /// Revoke all tokens for a user-client combination
        /// </summary>
        Task<bool> RevokeUserClientTokensAsync(string userId, string clientId, string reason);

        /// <summary>
        /// Get revocation history for audit
        /// </summary>
        Task<IEnumerable<TokenRevocationModel>> GetRevocationHistoryAsync(string userId);
    }

    public class TokenRevocationResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string ErrorDescription { get; set; }
    }

    /// <summary>
    /// RFC 7662 Token Introspection Response
    /// </summary>
    public class TokenIntrospectionResult
    {
        /// <summary>
        /// REQUIRED. Boolean indicator of whether or not the presented token is currently active.
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// OPTIONAL. The scope of the token.
        /// </summary>
        public string Scope { get; set; }

        /// <summary>
        /// OPTIONAL. The client identifier for the token.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// OPTIONAL. The username of the resource owner who authorized this token.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// OPTIONAL. The type of the token (e.g., "Bearer").
        /// </summary>
        public string TokenType { get; set; }

        /// <summary>
        /// OPTIONAL. The expiration time of the token in UTC epoch seconds.
        /// </summary>
        public long? Exp { get; set; }

        /// <summary>
        /// OPTIONAL. The time when the token was issued in UTC epoch seconds.
        /// </summary>
        public long? Iat { get; set; }

        /// <summary>
        /// OPTIONAL. The time when the token becomes not-before in UTC epoch seconds.
        /// </summary>
        public long? Nbf { get; set; }

        /// <summary>
        /// OPTIONAL. A JWT containing the claims about the token holder.
        /// </summary>
        public string Jwt { get; set; }

        /// <summary>
        /// OPTIONAL. The subject of the token.
        /// </summary>
        public string Sub { get; set; }

        /// <summary>
        /// OPTIONAL. The issuer of the token.
        /// </summary>
        public string Iss { get; set; }

        /// <summary>
        /// OPTIONAL. The audience of the token.
        /// </summary>
        public string Aud { get; set; }

        /// <summary>
        /// OPTIONAL. The JWT ID.
        /// </summary>
        public string Jti { get; set; }

        /// <summary>
        /// Error response (if token is invalid/revoked)
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Error description
        /// </summary>
        public string ErrorDescription { get; set; }
    }
}

