using Authentication.DomainService.Security.Contracts;
using Authentication.DomainService.Security.Models;

namespace Authentication.DomainService.Security.Repositories
{
    public interface ISecurityRepository
    {
        Task<IReadOnlyList<SessionGroupDto>> GetSessionGroupsAsync(string userId, string? clientId, bool activeOnly, CancellationToken ct);
        Task<SessionGroupDto?> GetSessionGroupAsync(string userId, string sessionId, CancellationToken ct);
        Task<RefreshTokenStatus?> GetRefreshTokenStatusAsync(string tokenId, CancellationToken ct);
        Task<IReadOnlyList<RefreshTokenRotationDto>> GetRotationHistoryAsync(string sessionId, CancellationToken ct);
        Task<IReadOnlyList<RevokedAccessTokenDto>> GetRevokedAccessTokensAsync(string userId, CancellationToken ct);
        Task<IdpSessionSummaryDto?> GetIdpSessionAsync(string userId, string? tenantId, CancellationToken ct);
        Task EnsureIndexesAsync(CancellationToken ct);
    }
}
