using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Configurations;
using Iam.DomainService.Resources;
using System.Text.RegularExpressions;

namespace Iam.DomainService.Users
{
    public class CreateUserValidator : AbstractValidator<CreateUserRequest>
    {
        private readonly BlocksContext? _securityContext;
        private readonly IUserRepository _userRepository;
        private readonly IIamConfigurationRepository _configurationRepository;
        private readonly IResourceRepository _resourceRepository;

        public CreateUserValidator(IUserRepository userRepository, IIamConfigurationRepository configurationRepository, IResourceRepository resourceRepository)
        {
            _securityContext = BlocksContext.GetContext();
            _userRepository = userRepository;
            _configurationRepository = configurationRepository;
            _resourceRepository = resourceRepository;

            RuleFor(u => u.FirstName).MaximumLength(150).WithMessage("Maximum character limit 150 exceeded").When(u => !string.IsNullOrWhiteSpace(u.FirstName));
            RuleFor(u => u.LastName).MaximumLength(150).WithMessage("Maximum character limit 150 exceeded").When(u => !string.IsNullOrWhiteSpace(u.LastName));
            RuleFor(u => u.UserName)
               .Cascade(CascadeMode.Stop)
               .Length(4, 100)
               .WithMessage("User name must be within 4 to 40 characters in length")
               .MustAsync(NotAnExistingUser)
               .WithMessage("User name already exists")
               .When(u => !string.IsNullOrWhiteSpace(u.UserName));
            RuleFor(u => u.Password)
                .Cascade(CascadeMode.Stop)
                .MustAsync(BeAStrongPassword)
                .WithMessage(
                    "Password weak. Ensure at least one lower and upper case letter, one special character, one digit and minimum 8 characters length")
                .MustAsync(CheckBlackListPassword).WithMessage("This password can not be used.")
                .When(u => !string.IsNullOrWhiteSpace(u.Password));

            RuleFor(u => u.Email)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .WithMessage("Email require")
                .Must(BeAValidEmail)
                .WithMessage("Email invalid")
                .MustAsync(BeAnUniqueEmail)
                .WithMessage("Email already in use");
            RuleFor(u => u.PhoneNumber)
                .Must(BeStartedWithPlusCharacter)
                .WithMessage("Phone number must start with a plus (+) character. E.g: +88017********")
                .MaximumLength(20).WithMessage("Maximum character limit 20 exceeded")
                .When(u => !string.IsNullOrWhiteSpace(u.PhoneNumber));
            RuleFor(u => u.UserPassType)
                .NotEmpty()
                .NotNull()
                .IsInEnum();
            RuleFor(u => u.UserMfaType)
                .NotEmpty()
                .NotNull()
                .IsInEnum()
                .When(x => x.MfaEnabled);
            RuleFor(u => u.UserCreationType)
                .NotEmpty()
                .NotNull()
                .IsInEnum();
            RuleFor(u => u.OrganizationId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .WithMessage("Organization id is required when multi organization is enabled")
                .MustAsync(BeAnExistingOrganization)
                .WithMessage("Organization does not exist")
                .WhenAsync(async (_, cancellationToken) => await IsMultiOrgEnabledAsync(cancellationToken));
        }

        private async Task<bool> IsMultiOrgEnabledAsync(CancellationToken cancellationToken)
        {
            var tenantConfig = await _resourceRepository.GetTenantConfigurationAsync();
            return tenantConfig?.IsMultiOrgEnabled ?? false;
        }

        private async Task<bool> BeAnExistingOrganization(string? organizationId, CancellationToken cancellationToken)
        {
            if (organizationId == "default")
            {
                return true;
            }

            var organization = await _resourceRepository.GetOrganizationById(organizationId);
            return organization != null;
        }

        private async Task<bool> NotAnExistingUser(CreateUserRequest model, string userName, CancellationToken cancellationToken)
        {
            var organizationId = ResolveOrganizationId(model);
            var user = await _userRepository.GetUserByUserNameOrgIdAsync(userName, organizationId);

            return user == null;
        }

        private async Task<bool> BeAStrongPassword(string password, CancellationToken cancellationToken)
        {
            var config = await _configurationRepository.GetConfigurationAsync();

            if (config == null || string.IsNullOrWhiteSpace(config.PasswordStrengthCheckerRegex)) return true;

            var doesThePasswordMetTenantPasswordComplexityRequirements = Regex.IsMatch(password, config.PasswordStrengthCheckerRegex, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));

            return doesThePasswordMetTenantPasswordComplexityRequirements;
        }

        private async Task<bool> CheckBlackListPassword(string password, CancellationToken cancellationToken)
        {
            var isExist = await _userRepository.CheckPasswordBlackListedAsync(password, _securityContext?.TenantId);
            return !isExist;
        }

        private static bool BeAValidEmail(string email)
        {
            string emailValidatorExpression =
                @"\A(?:[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)\Z";

            var emailValid = Regex.IsMatch(email, emailValidatorExpression, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));

            return emailValid;
        }

        private async Task<bool> BeAnUniqueEmail(CreateUserRequest model, string email, CancellationToken cancellationToken)
        {
            var organizationId = ResolveOrganizationId(model);
            var user = await _userRepository.GetUserByUserNameOrgIdAsync(email.ToLower(), organizationId);
            return user == null;
        }

        private static string ResolveOrganizationId(CreateUserRequest model)
        {
            if (!string.IsNullOrWhiteSpace(model.OrganizationId))
            {
                return model.OrganizationId;
            }

            var orgIdFromRoles = model.Roles.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));
            if (!string.IsNullOrWhiteSpace(orgIdFromRoles))
            {
                return orgIdFromRoles;
            }

            var orgIdFromPermissions = model.Permissions.FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));
            if (!string.IsNullOrWhiteSpace(orgIdFromPermissions))
            {
                return orgIdFromPermissions;
            }

            return "default";
        }

        private static bool BeStartedWithPlusCharacter(string phoneNumber)
        {
            var startedWithPlusCharacter = phoneNumber.StartsWith("+", StringComparison.InvariantCultureIgnoreCase);

            return startedWithPlusCharacter;
        }
    }
}
