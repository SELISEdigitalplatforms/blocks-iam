using FluentValidation;

namespace Iam.DomainService.SignupLinks;

public class CreateSignupLinkConfigurationValidator : AbstractValidator<CreateSignupLinkConfigurationRequest>
{
    public CreateSignupLinkConfigurationValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(100).WithMessage("Name must be at most 100 characters");

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description != null)
            .WithMessage("Description must be at most 500 characters");

        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("ClientId is required");

        RuleFor(x => x.RedirectUri)
            .NotEmpty().WithMessage("RedirectUri is required");

        RuleFor(x => x.CredentialMode)
            .NotNull().WithMessage("CredentialMode is required")
            .IsInEnum().WithMessage("CredentialMode is required");

        RuleFor(x => x.DefaultForwardedTo)
            .Must(SignupLinkConfigurationValidation.IsRelativeForwardedTo)
            .WithMessage("ForwardedTo must be a relative path");

        RuleFor(x => x.DefaultLifetimeMinutes)
            .InclusiveBetween(
                SignupLinkConfigurationValidation.MinLifetimeMinutes,
                SignupLinkConfigurationValidation.MaxLifetimeMinutes)
            .When(x => x.DefaultLifetimeMinutes.HasValue)
            .WithMessage("Must be between 5 and 10080");

        RuleFor(x => x.DefaultMaxRedemptions)
            .Must(SignupLinkConfigurationValidation.IsAllowedMaxRedemptions)
            .WithMessage("DefaultMaxRedemptions must be null or 1");

        RuleFor(x => x.DefaultPermissions)
            .Must(p => p == null || p.Count <= SignupLinkConfigurationValidation.MaxPermissions)
            .WithMessage($"DefaultPermissions cannot exceed {SignupLinkConfigurationValidation.MaxPermissions} entries");
    }
}
