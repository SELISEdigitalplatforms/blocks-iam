using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Services;

namespace Authentication.DomainService.Authentication
{
    /// <summary>Checks an MFA code with the wrong-code counter reserved first (see GuardedAttemptRunner).</summary>
    public interface IMfaAttemptGate
    {
        Task<GuardedAttempt> CheckAsync(string userId, IdentityConfiguration configuration, Func<Task<bool>> isCorrect);
    }
}
