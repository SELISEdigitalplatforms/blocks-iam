using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Configurations;
using Iam.DomainService.Services;

namespace Iam.DomainService.Accounts
{
    public abstract class PasswordValidator<T> : AbstractValidator<T>
    {
        protected readonly ITenants _tenants;
        protected readonly IIdentityAccessManagementRepository _identityAccessManagementRepository;
        private readonly IIamConfigurationRepository _configurationRepository;

        protected PasswordValidator(IIdentityAccessManagementRepository identityAccessManagementRepository, IIamConfigurationRepository configurationRepository)
        {
            _identityAccessManagementRepository = identityAccessManagementRepository;
            _configurationRepository = configurationRepository;
        }

        protected async Task<bool> BeAStrongPassword(string password, CancellationToken cancellationToken)
        {
            var config = await _configurationRepository.GetConfigurationAsync();

            return PasswordStrengthEvaluator.IsStrongPassword(config, password);
        }


        protected async Task<bool> CheckBlackListPassword(string password, CancellationToken cancellationToken)
        {
            // No tenant is read: the blacklist is global, so the check must run even on paths that
            // have no tenant context rather than treating the password as safe.
            var isExist = await _identityAccessManagementRepository.CheckPasswordBlackListedAsync(password);
            return !isExist;
        }
    }
}
