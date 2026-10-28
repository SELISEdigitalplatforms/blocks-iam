using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Services
{
    public enum GuardedAttemptResult
    {
        Valid,
        Invalid,
        Locked
    }

    /// <summary>
    /// The answer to one password or code check that was counted before it ran.
    /// <see cref="LockedNow"/> is the user when this wrong attempt applied the lock.
    /// </summary>
    public sealed record GuardedAttempt(GuardedAttemptResult Result, User? LockedNow = null)
    {
        public bool IsValid => Result == GuardedAttemptResult.Valid;

        /// <summary>Locked before the check, or locked by this wrong attempt.</summary>
        public bool IsLocked => Result == GuardedAttemptResult.Locked || LockedNow != null;
    }
}
