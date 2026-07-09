using Authentication.DomainService.Security.Contracts;
using Authentication.DomainService.Security.Models;

namespace Authentication.DomainService.Security.Repositories
{
    public interface ISecurityRepository
    {
        Task<IReadOnlyList<SessionDto>> GetSessionsAsync(string userId, string? tenantId, GetSessionsRequest req, CancellationToken ct);
        Task<SessionDto?> GetSessionAsync(string userId, string? tenantId, string sessionId, CancellationToken ct);
        Task<SessionDto?> GetSessionByIdAsync(string sessionId, CancellationToken ct);
        Task<SessionDto?> GetSessionByRefreshTokenAsync(string userId, string? tenantId, string refreshToken, CancellationToken ct);
        Task<RefreshTokenStatus?> GetRefreshTokenStatusAsync(string sessionId, CancellationToken ct);
        Task<IReadOnlyList<RevokedAccessTokenDto>> GetRevokedAccessTokensAsync(string userId, CancellationToken ct);
        Task<IReadOnlyList<AuthHistoryDto>> GetHistoryAsync(string userId, GetHistoryRequest req, CancellationToken ct);
        Task<IReadOnlyList<AuthHistoryDto>> GetSessionLifecycleAsync(string userId, string sessionId, CancellationToken ct);
        Task<IdpSessionSummaryDto?> GetIdpSessionAsync(string userId, string? tenantId, CancellationToken ct);
        Task<IReadOnlyList<ImpersonationSummaryDto>> GetImpersonationsAsync(string userId, CancellationToken ct);
        Task EnsureIndexesAsync(CancellationToken ct);
    }
}