using Authentication.DomainService.Entities;
using Authentication.DomainService.Shared.RequestModel;
using FluentValidation;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Authentication.DomainService.OAuth
{
    /// <summary>Validates the complete replacement payload for an OIDC UI template.</summary>
    public sealed class SaveOidcUiTemplateRequestValidator : AbstractValidator<SaveOidcUiTemplateRequest>
    {
        public SaveOidcUiTemplateRequestValidator()
        {
            RuleFor(x => x.Branding).NotNull().WithMessage("is required");
            RuleFor(x => x.Theme).NotNull().WithMessage("is required");
            RuleFor(x => x.Pages).NotNull().WithMessage("is required");

            When(x => x.Branding is not null, () =>
                RuleFor(x => x.Branding!).SetValidator(new BrandingValidator()));
            When(x => x.Theme is not null, () =>
                RuleFor(x => x.Theme!).SetValidator(new ThemeValidator()));
            When(x => x.Pages is not null, () =>
                RuleFor(x => x.Pages!).SetValidator(new PagesValidator()));
        }

        private sealed class BrandingValidator : AbstractValidator<OidcUiTemplateBranding>
        {
            public BrandingValidator()
            {
                RuleFor(x => x.BrandName).RequiredText(80);
                RuleFor(x => x.LogoUrl)
                    .Must(TemplateValidationRules.BeOptionalHttpUrl)
                    .WithMessage("must be an absolute http or https URL");
            }
        }

        private sealed class ThemeValidator : AbstractValidator<OidcUiTemplateTheme>
        {
            public ThemeValidator()
            {
                RuleFor(x => x.Light).NotNull().WithMessage("is required");
                RuleFor(x => x.Dark).NotNull().WithMessage("is required");

                When(x => x.Light is not null, () =>
                    RuleFor(x => x.Light!).SetValidator(new ThemePaletteValidator()));
                When(x => x.Dark is not null, () =>
                    RuleFor(x => x.Dark!).SetValidator(new ThemePaletteValidator()));
            }
        }

        private sealed class ThemePaletteValidator : AbstractValidator<OidcUiThemePalette>
        {
            public ThemePaletteValidator()
            {
                RuleFor(x => x.Primary).RequiredHexColor();
                RuleFor(x => x.Secondary).RequiredHexColor();
                RuleFor(x => x.Background).RequiredHexColor();
                RuleFor(x => x.Surface).RequiredHexColor();
                RuleFor(x => x.Text).RequiredHexColor();
                RuleFor(x => x.MutedText).RequiredHexColor();
                RuleFor(x => x.Success).RequiredHexColor();
                RuleFor(x => x.Danger).RequiredHexColor();

                RuleFor(x => x.Border).RequiredHexOrRgbaColor();
                RuleFor(x => x.BorderStrong).RequiredHexOrRgbaColor();
                RuleFor(x => x.AccentSoft).RequiredHexOrRgbaColor();
            }
        }

        private sealed class PagesValidator : AbstractValidator<OidcUiTemplatePages>
        {
            public PagesValidator()
            {
                RuleFor(x => x.Login).NotNull().WithMessage("is required");
                RuleFor(x => x.Signup).NotNull().WithMessage("is required");
                RuleFor(x => x.ForgotPassword).NotNull().WithMessage("is required");
                RuleFor(x => x.ResetPassword).NotNull().WithMessage("is required");
                RuleFor(x => x.Activation).NotNull().WithMessage("is required");
                RuleFor(x => x.Mfa).NotNull().WithMessage("is required");
                RuleFor(x => x.AccountSelector).NotNull().WithMessage("is required");
                RuleFor(x => x.Shared).NotNull().WithMessage("is required");

                When(x => x.Login is not null, () => RuleFor(x => x.Login!).SetValidator(new LoginValidator()));
                When(x => x.Signup is not null, () => RuleFor(x => x.Signup!).SetValidator(new SignupValidator()));
                When(x => x.ForgotPassword is not null, () => RuleFor(x => x.ForgotPassword!).SetValidator(new ForgotPasswordValidator()));
                When(x => x.ResetPassword is not null, () => RuleFor(x => x.ResetPassword!).SetValidator(new ResetPasswordValidator()));
                When(x => x.Activation is not null, () => RuleFor(x => x.Activation!).SetValidator(new ActivationValidator()));
                When(x => x.Mfa is not null, () => RuleFor(x => x.Mfa!).SetValidator(new MfaValidator()));
                When(x => x.AccountSelector is not null, () => RuleFor(x => x.AccountSelector!).SetValidator(new AccountSelectorValidator()));
                When(x => x.Shared is not null, () => RuleFor(x => x.Shared!).SetValidator(new SharedValidator()));
            }
        }

        private sealed class LoginValidator : AbstractValidator<OidcUiLoginPage>
        {
            public LoginValidator()
            {
                RuleFor(x => x.Heading).RequiredText(200);
                RuleFor(x => x.EmailLabel).RequiredText(200);
                RuleFor(x => x.PasswordLabel).RequiredText(200);
                RuleFor(x => x.ForgotPasswordLink).RequiredText(200);
                RuleFor(x => x.SubmitButton).RequiredText(200);
                RuleFor(x => x.SignupPrompt).RequiredText(200);
                RuleFor(x => x.SignupLink).RequiredText(200);
                RuleFor(x => x.ActivationErrorTitle).RequiredText(200);
                RuleFor(x => x.ActivationErrorMessage).RequiredText(200);
                RuleFor(x => x.ActivateAccountButton).RequiredText(200);
                RuleFor(x => x.BackToLoginButton).RequiredText(200);
            }
        }

        private sealed class SignupValidator : AbstractValidator<OidcUiSignupPage>
        {
            public SignupValidator()
            {
                RuleFor(x => x.Heading).RequiredText(200);
                RuleFor(x => x.FirstNameLabel).RequiredText(200);
                RuleFor(x => x.LastNameLabel).RequiredText(200);
                RuleFor(x => x.EmailLabel).RequiredText(200);
                RuleFor(x => x.SubmitButton).RequiredText(200);
                RuleFor(x => x.TermsPrefix).RequiredText(200);
                RuleFor(x => x.TermsLinkText).RequiredText(200);
                RuleFor(x => x.PrivacyLinkText).RequiredText(200);
                RuleFor(x => x.LoginPrompt).RequiredText(200);
                RuleFor(x => x.LoginLink).RequiredText(200);
                RuleFor(x => x.SuccessTitle).RequiredText(200);
                RuleFor(x => x.SuccessSubtitle).RequiredText(200);
            }
        }

        private sealed class ForgotPasswordValidator : AbstractValidator<OidcUiForgotPasswordPage>
        {
            public ForgotPasswordValidator()
            {
                RuleFor(x => x.Heading).RequiredText(200);
                RuleFor(x => x.EmailLabel).RequiredText(200);
                RuleFor(x => x.SubmitButton).RequiredText(200);
            }
        }

        private sealed class ResetPasswordValidator : AbstractValidator<OidcUiResetPasswordPage>
        {
            public ResetPasswordValidator()
            {
                RuleFor(x => x.Heading).RequiredText(200);
                RuleFor(x => x.PasswordLabel).RequiredText(200);
                RuleFor(x => x.ConfirmPasswordLabel).RequiredText(200);
                RuleFor(x => x.LogoutFromDevicesLabel).RequiredText(200);
                RuleFor(x => x.SubmitButton).RequiredText(200);
                RuleFor(x => x.SuccessTitle).RequiredText(200);
                RuleFor(x => x.SuccessSubtitle).RequiredText(200);
            }
        }

        private sealed class ActivationValidator : AbstractValidator<OidcUiActivationPage>
        {
            public ActivationValidator()
            {
                RuleFor(x => x.Heading).RequiredText(200);
                RuleFor(x => x.PasswordLabel).RequiredText(200);
                RuleFor(x => x.ConfirmPasswordLabel).RequiredText(200);
                RuleFor(x => x.SubmitButton).RequiredText(200);
                RuleFor(x => x.SuccessTitle).RequiredText(200);
                RuleFor(x => x.SuccessSubtitle).RequiredText(200);
            }
        }

        private sealed class MfaValidator : AbstractValidator<OidcUiMfaPage>
        {
            public MfaValidator()
            {
                RuleFor(x => x.Heading).RequiredText(200);
                RuleFor(x => x.SubmitButton).RequiredText(200);
                RuleFor(x => x.ResendButton).OptionalText(200);
            }
        }

        private sealed class AccountSelectorValidator : AbstractValidator<OidcUiAccountSelectorPage>
        {
            public AccountSelectorValidator()
            {
                RuleFor(x => x.Heading).RequiredText(200);
                RuleFor(x => x.Subheading).OptionalText(200);
            }
        }

        private sealed class SharedValidator : AbstractValidator<OidcUiSharedPage>
        {
            public SharedValidator()
            {
                RuleFor(x => x.FooterText).RequiredText(200);
            }
        }
    }

    internal static class TemplateValidationRules
    {
        private const string HexColorMessage = "must be a valid hex color (#RGB or #RRGGBB)";
        private const string HexOrRgbaColorMessage = "must be a valid hex color (#RGB or #RRGGBB) or rgba(r,g,b,a) color";

        private static readonly Regex HexColorRegex = new(
            "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        private static readonly Regex RgbaColorRegex = new(
            @"^rgba\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*,\s*(?<a>(?:\d+(?:\.\d+)?|\.\d+))\s*\)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));

        public static IRuleBuilderOptions<T, string?> RequiredText<T>(
            this IRuleBuilder<T, string?> rule,
            int maximumLength)
        {
            return rule
                .Must(value => !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength)
                .WithMessage($"must be between 1 and {maximumLength} characters");
        }

        public static IRuleBuilderOptions<T, string?> OptionalText<T>(
            this IRuleBuilder<T, string?> rule,
            int maximumLength)
        {
            return rule
                .Must(value => value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength))
                .WithMessage($"must be between 1 and {maximumLength} characters");
        }

        public static IRuleBuilderOptions<T, string?> RequiredHexColor<T>(this IRuleBuilder<T, string?> rule)
        {
            return rule
                .Must(value => value is not null && HexColorRegex.IsMatch(value))
                .WithMessage(HexColorMessage);
        }

        public static IRuleBuilderOptions<T, string?> RequiredHexOrRgbaColor<T>(this IRuleBuilder<T, string?> rule)
        {
            return rule
                .Must(value => value is not null && (HexColorRegex.IsMatch(value) || IsRgbaColor(value)))
                .WithMessage(HexOrRgbaColorMessage);
        }

        public static bool BeOptionalHttpUrl(string? value)
        {
            if (value is null)
            {
                return true;
            }

            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrWhiteSpace(uri.Host);
        }

        private static bool IsRgbaColor(string value)
        {
            var match = RgbaColorRegex.Match(value);
            if (!match.Success)
            {
                return false;
            }

            return byte.TryParse(match.Groups["r"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                && byte.TryParse(match.Groups["g"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                && byte.TryParse(match.Groups["b"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                && double.TryParse(match.Groups["a"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var alpha)
                && alpha is >= 0 and <= 1;
        }
    }
}
