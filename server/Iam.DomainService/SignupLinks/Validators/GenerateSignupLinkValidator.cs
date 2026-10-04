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

        // A redirect URI is meaningful only against a client, so the override moves as a
        // pair -- resolving them independently would let a payload redirect be checked
        // against a configuration client it was never meant for.
        RuleFor(x => x.RedirectUri)
            .Must((req, _) => string.IsNullOrWhiteSpace(req.ClientId) == string.IsNullOrWhiteSpace(req.RedirectUri))
            .WithMessage("Supply clientId and redirectUri together, or neither");

        RuleFor(x => x.ForwardedTo)
            .Must(SignupLinkConfigurationValidation.IsRelativeForwardedTo)
            .WithMessage("ForwardedTo must be a relative path");

        RuleFor(x => x.MaxRedemptions)
            .Must(SignupLinkConfigurationValidation.IsAllowedMaxRedemptions)
            .WithMessage("MaxRedemptions must be 0 (unlimited) or a positive count");

        RuleFor(x => x.ExpiresInMinutes)
            .InclusiveBetween(
                SignupLinkConfigurationValidation.MinLifetimeMinutes,
                SignupLinkConfigurationValidation.MaxLifetimeMinutes)
            .When(x => x.ExpiresInMinutes.HasValue)
            .WithMessage("Must be between 5 and 10080");
    }
}
