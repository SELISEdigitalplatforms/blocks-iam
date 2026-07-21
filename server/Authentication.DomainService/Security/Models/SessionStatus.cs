namespace Authentication.DomainService.Security.Models
{
    /// <summary>
    /// Lifecycle state of an authenticated session as recorded in the security
    /// timeline. Transitions are monotonic: <see cref="Active"/> → <see cref="Expired"/>
    /// or <see cref="Revoked"/>.
    /// </summary>
    public enum SessionStatus
    {
        /// <summary>Session is valid and tokens issued under it can be used.</summary>
        Active = 0,

        /// <summary>Session has passed its absolute expiry and is no longer accepted.</summary>
        Expired = 1,

        /// <summary>Session was terminated by an explicit logout, password change, or admin action.</summary>
        Revoked = 2,
    }
}