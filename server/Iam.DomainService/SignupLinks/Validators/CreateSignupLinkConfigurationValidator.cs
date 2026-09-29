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

        // Client and redirect belong to OIDC mode only. Embedded mode has no client
        // registration to validate a redirect against, which is exactly why its redemption
        // never redirects (SPEC26 A2).
        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("ClientId is required in OIDC mode")
            .When(x => SignupLinkConfigurationValidation.IsOidc(x.Mode));

        RuleFor(x => x.RedirectUri)
            .NotEmpty().WithMessage("RedirectUri is required in OIDC mode")
            .When(x => SignupLinkConfigurationValidation.IsOidc(x.Mode));

        RuleFor(x => x.ClientId)
            .Empty().WithMessage("ClientId must be empty in embedded mode")
            .When(x => !SignupLinkConfigurationValidation.IsOidc(x.Mode));

        RuleFor(x => x.RedirectUri)
            .Empty().WithMessage("RedirectUri must be empty in embedded mode")
            .When(x => !SignupLinkConfigurationValidation.IsOidc(x.Mode));

        RuleFor(x => x.Mode)
            .IsInEnum().WithMessage("Mode is required")
            .When(x => x.Mode.HasValue);

        RuleFor(x => x.JoinUrl)
            .Must(SignupLinkConfigurationValidation.IsValidJoinUrl)
            .WithMessage("JoinUrl must be an absolute https URL with no query or fragment");

        RuleFor(x => x.JoinUrl)
            .Empty().WithMessage("JoinUrl applies only to embedded configurations")
            .When(x => SignupLinkConfigurationValidation.IsOidc(x.Mode));

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
