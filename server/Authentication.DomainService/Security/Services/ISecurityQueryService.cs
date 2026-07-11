using Authentication.DomainService.Security.Models;

namespace Authentication.DomainService.Security.Services
{
    public interface ISecurityQueryService
    {
        Task<SecuritySummaryDto> GetSecuritySummaryAsync(string userId, CancellationToken ct);

        Task<IReadOnlyList<UserSessionDto>> GetUserSessionsAsync(string userId, CancellationToken ct);

        Task<SessionDetailsDto> GetSessionDetailsAsync(
            string userId,
            string sessionId,
            CancellationToken ct);

        Task<string?> ResolveCurrentSessionIdAsync(string userId, CancellationToken ct);
    }
}
