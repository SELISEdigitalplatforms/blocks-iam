using FluentValidation;

namespace Iam.DomainService.Users
{
    public class GetUsersRequestValidator : AbstractValidator<GetUsersRequest>
    {
        public GetUsersRequestValidator()
        {
            RuleFor(x => x.Filter)
                .NotNull()
                .WithMessage("Filter is required.");

            RuleFor(x => x.Filter)
                .Must(filter => !string.IsNullOrWhiteSpace(filter.Email) || !string.IsNullOrWhiteSpace(filter.Name))
                .WithMessage("At least one of Email or Name must be provided.")
                .When(x => x.Filter != null);
        }
    }
}
