namespace Iam.DomainService.Services
{
    /// <summary>
    /// Writes the stamps Genesis reads to refuse access tokens minted before a change, so a removed
    /// role, a disabled organization or an organization switch takes effect on the next request
    /// instead of when the access token expires. Never throws: the change is already saved, and the
    /// refresh still re-reads the user's membership.
    ///
    /// Declared here rather than in Authentication.DomainService for the same reason as
    /// <see cref="IUserSessionTerminator"/>: the reverse reference would be circular.
    /// </summary>
    public interface IAccessStampWriter
    {
        /// <summary>The user lost a role, permission or organization in the current tenant.</summary>
        Task StampUserAsync(string userId);

        /// <summary>The organization was disabled in the current tenant.</summary>
        Task StampOrganizationAsync(string organizationId);

        /// <summary>The sign-in session moved to another organization: one organization at a time.</summary>
        Task StampSessionAsync(string sessionId);
    }
}
