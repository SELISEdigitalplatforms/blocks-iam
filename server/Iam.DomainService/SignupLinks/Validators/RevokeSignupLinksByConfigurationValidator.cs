using FluentValidation;

namespace Iam.DomainService.SignupLinks;

public class RevokeSignupLinksByConfigurationValidator : AbstractValidator<RevokeSignupLinksByConfigurationRequest>
{
    public RevokeSignupLinksByConfigurationValidator()
    {
        RuleFor(x => x.ConfigurationId)
            .NotEmpty().WithMessage("ConfigurationId is required");
    }
}
