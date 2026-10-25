using Idp.DomainService.Oidc.Contracts;

namespace Authentication.DomainService.Oidc.Repositories;

public interface ILinkSessionRepository
{
    /// <summary>Stores the session under <see cref="LinkSessionModel.TenantId"/>.</summary>
    Task CreateAsync(LinkSessionModel session);

    /// <summary>Reads a session (many times while it lives). No delete on read.</summary>
    Task<LinkSessionModel?> GetBySessionIdAsync(string tenantId, string sessionId);

    /// <summary>Ends a session: removes the Redis key and the old MongoDB row of older pods.</summary>
    Task DeleteAsync(string tenantId, string sessionId);
}
