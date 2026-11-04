using Authentication.DomainService.Security.Models;

namespace Authentication.DomainService.Security.Services
{
    public interface ISessionRevocationService
    {
        /// <summary>
        /// Revokes <paramref name="sessionId"/> of <paramref name="userId"/>. Warning <c>session_not_found</c>
        /// when that user has no such session; nothing is revoked then.
        /// </summary>
        Task<RevokeSessionResponse> RevokeSessionAsync(string sessionId, string userId, string currentSessionId, string? reason, CancellationToken ct);

        /// <summary>
        /// Revokes one refresh token of <paramref name="userId"/>. Warning <c>refresh_token_not_found</c>
        /// when the token is unknown or belongs to someone else; nothing is revoked then.
        /// </summary>
        Task<RevokeSessionResponse> RevokeRefreshTokenAsync(string tokenId, string userId, string currentSessionId, string? reason, CancellationToken ct);
    }
}