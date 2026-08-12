namespace Iam.DomainService.Services
{
    /// <summary>
    /// Resolves the OIDC client an account-action email (activation, recovery) should
    /// send the user back to when the originating request carried no client of its own —
    /// i.e. a user invited from the Blocks OS portal rather than self-service signup.
    ///
    /// Declared here rather than in Authentication.DomainService because the email
    /// builders live in this assembly, and Authentication.DomainService already
    /// references it; the reverse reference would be circular.
    /// </summary>
    public interface IDefaultOidcClientResolver
    {
        /// <summary>
        /// The tenant's first active OIDC client that has a redirect URI registered,
        /// or null when the tenant has none.
        /// </summary>
        Task<DefaultOidcClient?> GetDefaultClientAsync();
    }

    /// <param name="ClientId">Public OIDC client identifier.</param>
    /// <param name="RedirectUri">A redirect URI already registered against that client.</param>
    public sealed record DefaultOidcClient(string ClientId, string RedirectUri);
}
