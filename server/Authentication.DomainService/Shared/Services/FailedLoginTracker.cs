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

        public async Task<User?> RecordFailedAttemptAsync(string userId)
        {
            var configuration = await _repository.GetAuthenticationConfigurationAsync();
            if (configuration == null)
            {
                return null;
            }

            return await _repository.IncrementFailedLoginAndApplyLockoutAsync(
                userId,
                configuration.GetNumberOfWrongAttemptsToLockTheAccount,
                configuration.AccountLockDurationInMinutes,
                DateTime.UtcNow);
        }
    }
}
