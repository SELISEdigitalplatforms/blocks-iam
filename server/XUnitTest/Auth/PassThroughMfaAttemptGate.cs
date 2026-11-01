using Authentication.DomainService.Authentication;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Services;

namespace XUnitTest.Auth
{
    /// <summary>
    /// Runs the code check as the real gate does, without the counter. Set <see cref="LockedBeforeCheck"/>
    /// or <see cref="LockOnWrongCode"/> to play a locked account.
    /// </summary>
    public sealed class PassThroughMfaAttemptGate : IMfaAttemptGate
    {
        public bool LockedBeforeCheck { get; set; }
        public bool LockOnWrongCode { get; set; }
        public int Checks { get; private set; }
        public string? LastUserId { get; private set; }

        public async Task<GuardedAttempt> CheckAsync(string userId, IdentityConfiguration configuration, Func<Task<bool>> isCorrect)
        {
            LastUserId = userId;
            if (LockedBeforeCheck)
            {
                return new GuardedAttempt(GuardedAttemptResult.Locked);
            }

            Checks++;
            if (await isCorrect())
            {
                return new GuardedAttempt(GuardedAttemptResult.Valid);
            }

            return LockOnWrongCode
                ? new GuardedAttempt(GuardedAttemptResult.Invalid, new User { ItemId = userId, LockoutUntilUtc = DateTime.UtcNow.AddMinutes(15) })
                : new GuardedAttempt(GuardedAttemptResult.Invalid);
        }
    }
}
