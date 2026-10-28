using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Services;

namespace Authentication.DomainService.Services
{
    /// <summary>
    /// Supplies <see cref="IFailedLoginTracker"/> to Iam.DomainService over the same repository
    /// call and configuration values the login path uses.
    /// </summary>
    public sealed class FailedLoginTracker : IFailedLoginTracker
    {
        private readonly IAuthenticationRepository _repository;

        public FailedLoginTracker(IAuthenticationRepository repository)
        {
            _repository = repository;
        }

        public async Task<GuardedAttempt> CheckPasswordAsync(string userId, Func<bool> isCorrect)
        {
            var configuration = await _repository.GetAuthenticationConfigurationAsync() ?? new IdentityConfiguration();
            var threshold = configuration.GetNumberOfWrongAttemptsToLockTheAccount;

            return await GuardedAttemptRunner.RunAsync(
                threshold,
                () => _repository.TryReserveLoginAttemptAsync(userId, threshold, DateTime.UtcNow),
                () => Task.FromResult(isCorrect()),
                () => _repository.ReleaseLoginAttemptAsync(userId, DateTime.UtcNow),
                user => user.FailedLoginCount,
                () => _repository.LockAccountIfLoginThresholdReachedAsync(userId, threshold, configuration.AccountLockDurationInMinutes, DateTime.UtcNow));
        }
    }
}
