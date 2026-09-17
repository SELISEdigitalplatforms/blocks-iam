using FluentValidation;
using Iam.DomainService.Configurations;
using Iam.DomainService.Services;

namespace Iam.DomainService.Accounts
{
    public class ChangePasswordValidator : PasswordValidator<ChangePasswordRequest>
    {
        public ChangePasswordValidator(IIamConfigurationRepository configurationRepository, IIdentityAccessManagementRepository identityAccessManagementRepository)
            : base(identityAccessManagementRepository, configurationRepository)
        {
            RuleFor(u => u.OldPassword)
                .NotEmpty()
                .NotNull();

            RuleFor(u => u.NewPassword)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .MustAsync(BeAStrongPassword)
                .WithMessage("Does not meet project's password requirements")
                .MustAsync(CheckBlackListPassword)
                .WithMessage("This password can not be used.");
        }
    }
}
