using Authentication.DomainService.Services;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Services;

namespace Authentication.DomainService.Authentication
{
    public sealed class MfaAttemptGate : IMfaAttemptGate
    {
        private readonly IAuthenticationRepository _repository;

        public MfaAttemptGate(IAuthenticationRepository repository)
        {
            _repository = repository;
        }

        public Task<GuardedAttempt> CheckAsync(string userId, IdentityConfiguration configuration, Func<Task<bool>> isCorrect)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            var threshold = configuration.GetNumberOfWrongAttemptsToLockTheAccount;

            return GuardedAttemptRunner.RunAsync(
                threshold,
                () => _repository.TryReserveMfaAttemptAsync(userId, threshold, DateTime.UtcNow),
                isCorrect,
                () => _repository.ReleaseMfaAttemptAsync(userId, DateTime.UtcNow),
                user => user.FailedMfaCount,
                () => _repository.LockAccountIfMfaThresholdReachedAsync(userId, threshold, configuration.AccountLockDurationInMinutes, DateTime.UtcNow));
        }
    }
}
