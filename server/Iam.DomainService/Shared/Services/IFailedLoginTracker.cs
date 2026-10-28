namespace Iam.DomainService.Services
{
    /// <summary>
    /// Checks a password on the shared login counter and applies the lockout (threshold and
    /// duration from the tenant's identity configuration). Change-password uses it so a wrong
    /// old password counts and locks exactly like a wrong login password.
    ///
    /// Declared here, implemented in Authentication.DomainService, for the same reason as
    /// <see cref="IUserSessionTerminator"/>: the reverse reference would be circular.
    /// </summary>
    public interface IFailedLoginTracker
    {
        /// <summary>
        /// Counts the attempt first, then runs <paramref name="isCorrect"/>, then gives the attempt back when
        /// it was right. Parallel wrong passwords get no more checks than the lock threshold.
        /// </summary>
        Task<GuardedAttempt> CheckPasswordAsync(string userId, Func<bool> isCorrect);
    }
}
