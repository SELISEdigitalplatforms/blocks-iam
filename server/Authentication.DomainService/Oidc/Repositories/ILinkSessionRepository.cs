using Idp.DomainService.Oidc.Contracts;

namespace Authentication.DomainService.Oidc.Repositories;

public interface ILinkSessionRepository
{
    Task EnsureIndexesAsync(CancellationToken ct = default);
    Task CreateAsync(LinkSessionModel session);
    Task<LinkSessionModel?> GetBySessionIdAsync(string sessionId);
}
