using Authentication.DomainService.Security.Contracts;
using Authentication.DomainService.Security.Models;

namespace Authentication.DomainService.Security.Services
{
    public interface ISecurityQueryService
    {
        Task<SecurityOverviewDto> GetSecurityOverviewAsync(string userId, CancellationToken ct);
        Task<SessionTimelineDto> GetSessionTimelineAsync(string userId, string sessionId, CancellationToken ct);
        Task<IReadOnlyList<SessionGroupDto>> GetSessionGroupsAsync(string userId, CancellationToken ct);
        Task<SessionGroupDto?> GetSessionGroupAsync(string userId, string sessionId, CancellationToken ct);
        Task<IReadOnlyList<RefreshTokenRotationDto>> GetRotationHistoryAsync(string sessionId, CancellationToken ct);
        Task<string?> ResolveCurrentSessionIdAsync(string userId, CancellationToken ct);
    }
}