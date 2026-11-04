using FluentValidation;

namespace Iam.DomainService.SignupLinks;

public class SignupLinkSummaryValidator : AbstractValidator<SignupLinkSummaryRequest>
{
    public SignupLinkSummaryValidator()
    {
        RuleFor(x => x.ConfigurationId)
            .Must(id => !string.IsNullOrWhiteSpace(id))
            .WithMessage("ConfigurationId is required");
    }
}
