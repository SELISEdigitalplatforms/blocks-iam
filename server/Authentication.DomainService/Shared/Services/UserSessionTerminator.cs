using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Blocks.Genesis;
using Iam.DomainService.Services;

namespace Authentication.DomainService.Services
{
    /// <summary>
    /// Supplies <see cref="IUserSessionTerminator"/> to Iam.DomainService; the logout-all worker
    /// uses it too, so the inline path and the queued backup end exactly the same things.
    /// </summary>
    public sealed class UserSessionTerminator : IUserSessionTerminator
    {
        private readonly ITokenRevocationService _tokenRevocationService;
        private readonly IIdpSessionService _idpSessionService;

        public UserSessionTerminator(ITokenRevocationService tokenRevocationService, IIdpSessionService idpSessionService)
        {
            _tokenRevocationService = tokenRevocationService;
            _idpSessionService = idpSessionService;
        }

        public async Task EndAllSessionsAsync(string userId, string reason)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("User and tenant are required to end sessions.");
            }

            // Mongo and the Redis copy, which the refresh resolver trusts first.
            await _tokenRevocationService.RevokeAllUserTokensAsync(userId, tenantId, reason);

            // Only this user's account leaves each IdP session; other users signed in on the same
            // browser keep theirs (the session is deleted once its last account is gone).
            var sessions = await _idpSessionService.GetUserSessionsAsync(userId, tenantId) ?? [];
            foreach (var session in sessions.Where(s => s != null && !s.RevokedAt.HasValue))
            {
                await _idpSessionService.RemoveAccountAsync(session.SessionId, userId, tenantId);
            }
        }
    }
}
