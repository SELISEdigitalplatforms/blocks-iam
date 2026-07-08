using Blocks.Genesis;
using Authentication.DomainService.Security.Contracts;
using Authentication.DomainService.Security.Models;

namespace Authentication.DomainService.Security.Services
{
    public interface ISecurityQueryService
    {
        Task<BaseQueryListResponse<IQueryable<SessionDto>>> GetSessionsAsync(GetSessionsRequest req, CancellationToken ct);
        Task<SessionDto?> GetSessionAsync(string sessionId, CancellationToken ct);
        Task<BaseQueryListResponse<IQueryable<AuthHistoryDto>>> GetHistoryAsync(GetHistoryRequest req, CancellationToken ct);
        Task<SessionTimelineDto?> GetSessionTimelineAsync(string sessionId, CancellationToken ct);
        Task<IdpSessionSummaryDto?> GetIdpSessionAsync(CancellationToken ct);
        Task<List<ImpersonationSummaryDto>> GetImpersonationsAsync(CancellationToken ct);
        Task<string?> ResolveCurrentSessionIdAsync(string userId, CancellationToken ct);
    }
}