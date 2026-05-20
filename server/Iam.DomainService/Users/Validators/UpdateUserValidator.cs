using Blocks.Genesis;
using FluentValidation;

namespace Iam.DomainService.Users
{
    public class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
    {
        private readonly ITenants _tenants;

        public UpdateUserValidator(ITenants tenants)
        {
            _tenants = tenants;

            RuleFor(u => u.ItemId).NotEmpty().NotNull();
            RuleFor(u => u.FirstName).MaximumLength(150).WithMessage("Maximum character limit 150 exceeded").When(u => !string.IsNullOrWhiteSpace(u.FirstName));
            RuleFor(u => u.LastName).MaximumLength(150).WithMessage("Maximum character limit 150 exceeded").When(u => !string.IsNullOrWhiteSpace(u.LastName));
        }

    }
}
