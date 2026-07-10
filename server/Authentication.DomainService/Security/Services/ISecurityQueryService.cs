using Blocks.Genesis;
using Authentication.DomainService.Security.Contracts;
using Authentication.DomainService.Security.Models;

namespace Authentication.DomainService.Security.Services
{
    public interface ISecurityQueryService
    {
        Task<BaseQueryListResponse<IQueryable<SessionDto>>> GetSessionsAsync(string targetUserId, GetSessionsRequest req, CancellationToken ct);
        Task<SessionDto?> GetSessionAsync(string targetUserId, string sessionId, CancellationToken ct);
        Task<SessionDto?> GetSessionByIdAsync(string sessionId, CancellationToken ct);
        Task<IdpSessionSummaryDto?> GetIdpSessionAsync(string targetUserId, CancellationToken ct);
        Task<List<ImpersonationSummaryDto>> GetImpersonationsAsync(string targetUserId, CancellationToken ct);
        Task<string?> ResolveCurrentSessionIdAsync(string userId, CancellationToken ct);
    }
}