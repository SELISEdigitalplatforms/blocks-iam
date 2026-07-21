using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Configurations;
using Iam.DomainService.Services;
using System.Text.RegularExpressions;

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

            if (config == null || string.IsNullOrWhiteSpace(config.PasswordStrengthCheckerRegex))
                return true;

            try
            {
                return Regex.IsMatch(password, config.PasswordStrengthCheckerRegex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
            }
            catch (RegexMatchTimeoutException)
            {
                return false; // Consider the password invalid if the regex check times out
            }
        }


        protected async Task<bool> CheckBlackListPassword(string password, CancellationToken cancellationToken)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;
            var isExist = await _identityAccessManagementRepository.CheckPasswordBlackListedAsync(password, tenantId);
            return !isExist;
        }
    }
}
