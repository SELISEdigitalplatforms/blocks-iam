using Authentication.DomainService.Security.Models;

namespace Authentication.DomainService.Security.Services
{
    public interface ISessionRevocationService
    {
        Task<RevokeSessionResponse> RevokeSessionAsync(string sessionId, string actorUserId, string currentSessionId, string? reason, CancellationToken ct);
    }
}