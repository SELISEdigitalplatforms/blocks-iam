using FluentValidation;

namespace Iam.DomainService.SignupLinks;

public class GenerateSignupLinkValidator : AbstractValidator<GenerateSignupLinkRequest>
{
    public GenerateSignupLinkValidator()
    {
        RuleFor(x => x.ConfigurationId)
            .NotEmpty().WithMessage("ConfigurationId is required");

        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email is not valid");

        RuleFor(x => x.FirstName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("FirstName is required")
            .Length(1, 100).WithMessage("FirstName must be between 1 and 100 characters");

        RuleFor(x => x.LastName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("LastName is required")
            .Length(1, 100).WithMessage("LastName must be between 1 and 100 characters");

        RuleFor(x => x.ForwardedTo)
            .Must(SignupLinkConfigurationValidation.IsRelativeForwardedTo)
            .WithMessage("ForwardedTo must be a relative path");

        RuleFor(x => x.ExpiresInMinutes)
            .InclusiveBetween(
                SignupLinkConfigurationValidation.MinLifetimeMinutes,
                SignupLinkConfigurationValidation.MaxLifetimeMinutes)
            .When(x => x.ExpiresInMinutes.HasValue)
            .WithMessage("Must be between 5 and 10080");
    }
}
