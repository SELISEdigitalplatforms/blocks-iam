using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Services;

namespace Authentication.DomainService.Services
{
    /// <summary>
    /// Count first, check second: the attempt is reserved as a failure before the check runs and
    /// given back only when it was right. So a burst of parallel guesses gets no more checks than
    /// the lock threshold, and the request that took the last slot applies the lock.
    /// </summary>
    internal static class GuardedAttemptRunner
    {
        public static async Task<GuardedAttempt> RunAsync(
            int threshold,
            Func<Task<User?>> reserve,
            Func<Task<bool>> check,
            Func<Task<bool>> release,
            Func<User, int> countAfterReserve,
            Func<Task<User?>> lockIfThresholdReached)
        {
            var reserved = await reserve();
            if (reserved == null)
            {
                return new GuardedAttempt(GuardedAttemptResult.Locked);
            }

            bool correct;
            try
            {
                correct = await check();
            }
            catch
            {
                // A failed check is not a wrong guess.
                await release();
                throw;
            }

            if (correct)
            {
                // False when parallel wrong guesses locked the account meanwhile.
                return new GuardedAttempt(await release() ? GuardedAttemptResult.Valid : GuardedAttemptResult.Locked);
            }

            var lockedNow = countAfterReserve(reserved) >= threshold ? await lockIfThresholdReached() : null;
            return new GuardedAttempt(GuardedAttemptResult.Invalid, lockedNow);
        }
    }
}
