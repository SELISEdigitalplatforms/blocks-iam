namespace Iam.DomainService.Services
{
    /// <summary>
    /// Ends every session of a user in the current tenant (refresh tokens and IdP session
    /// accounts). Password reset / change call it inline so the old sessions are dead before
    /// the response goes out.
    ///
    /// Declared here rather than in Authentication.DomainService for the same reason as
    /// <see cref="IDefaultOidcClientResolver"/>: AccountService lives in this assembly, and
    /// the reverse reference would be circular.
    /// </summary>
    public interface IUserSessionTerminator
    {
        Task EndAllSessionsAsync(string userId, string reason);
    }
}
