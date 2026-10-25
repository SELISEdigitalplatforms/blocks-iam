using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Services
{
    /// <summary>
    /// Counts one failed password attempt on the shared login counter and applies the lockout
    /// (threshold and duration from the tenant's identity configuration). Change-password uses
    /// it so a wrong old password counts and locks exactly like a wrong login password.
    ///
    /// Declared here, implemented in Authentication.DomainService, for the same reason as
    /// <see cref="IUserSessionTerminator"/>: the reverse reference would be circular.
    /// </summary>
    public interface IFailedLoginTracker
    {
        /// <returns>The user after the update (LockoutUntilUtc set when this attempt locked it), or null if not found.</returns>
        Task<User?> RecordFailedAttemptAsync(string userId);
    }
}
