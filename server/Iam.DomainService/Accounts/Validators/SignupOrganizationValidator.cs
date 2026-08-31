using FluentValidation;
using Iam.DomainService.Shared.Entities;
using System.Text.RegularExpressions;

namespace Iam.DomainService.Accounts
{
    /// <summary>
    /// Shape and size checks for the organization profile supplied at signup.
    /// <para>
    /// This runs on an <c>[AllowAnonymous]</c> path, so the length caps are a write-size guard
    /// as much as a correctness one. Name availability is NOT checked here — that stays in
    /// <c>CreateOrganizationAsync</c>, which is the only place that can decide it without a
    /// check-then-act race.
    /// </para>
    /// </summary>
    public class SignupOrganizationValidator : AbstractValidator<SignupOrganizationInfo>
    {
        private const int MaxAddresses = 5;
        private const int MaxNameLength = 150;
        private const int MaxDescriptionLength = 500;
        private const int MaxUrlLength = 2048;

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

        public SignupOrganizationValidator()
        {
            RuleFor(o => o.Name)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .WithMessage("Organization name is required.")
                .MaximumLength(MaxNameLength)
                .WithMessage($"Maximum character limit {MaxNameLength} exceeded");

            RuleFor(o => o.Description)
                .MaximumLength(MaxDescriptionLength)
                .WithMessage($"Maximum character limit {MaxDescriptionLength} exceeded")
                .When(o => !string.IsNullOrWhiteSpace(o.Description));

            RuleFor(o => o.Email)
                .Must(BeAValidEmail)
                .WithMessage("Email invalid")
                .When(o => !string.IsNullOrWhiteSpace(o.Email));

            RuleFor(o => o.PhoneNumber)
                .Must(value => value!.StartsWith('+'))
                .WithMessage("Phone number must start with a plus (+) character. E.g: +88017********")
                .MaximumLength(20)
                .WithMessage("Maximum character limit 20 exceeded")
                .When(o => !string.IsNullOrWhiteSpace(o.PhoneNumber));

            RuleFor(o => o.WebsiteUrl)
                .Must(BeAnAbsoluteHttpUrl)
                .WithMessage("Website url must be an absolute http(s) url")
                .MaximumLength(MaxUrlLength)
                .WithMessage($"Maximum character limit {MaxUrlLength} exceeded")
                .When(o => !string.IsNullOrWhiteSpace(o.WebsiteUrl));

            RuleFor(o => o.LogoUrl)
                .Must(BeAnAbsoluteHttpUrl)
                .WithMessage("Logo url must be an absolute http(s) url")
                .MaximumLength(MaxUrlLength)
                .WithMessage($"Maximum character limit {MaxUrlLength} exceeded")
                .When(o => !string.IsNullOrWhiteSpace(o.LogoUrl));

            RuleFor(o => o.Industry)
                .MaximumLength(100)
                .WithMessage("Maximum character limit 100 exceeded")
                .When(o => !string.IsNullOrWhiteSpace(o.Industry));

            RuleFor(o => o.TimeZone)
                .Must(BeAKnownTimeZone)
                .WithMessage("Time zone is not recognised")
                .When(o => !string.IsNullOrWhiteSpace(o.TimeZone));

            RuleFor(o => o.Currency)
                .Length(3)
                .WithMessage("Currency must be a 3-letter code. E.g: CHF")
                .When(o => !string.IsNullOrWhiteSpace(o.Currency));

            RuleFor(o => o.Locale)
                .MaximumLength(35)
                .WithMessage("Maximum character limit 35 exceeded")
                .When(o => !string.IsNullOrWhiteSpace(o.Locale));

            RuleFor(o => o.DateFormat)
                .MaximumLength(30)
                .WithMessage("Maximum character limit 30 exceeded")
                .When(o => !string.IsNullOrWhiteSpace(o.DateFormat));

            RuleFor(o => o.TimeFormat)
                .MaximumLength(30)
                .WithMessage("Maximum character limit 30 exceeded")
                .When(o => !string.IsNullOrWhiteSpace(o.TimeFormat));

            RuleFor(o => o.Addresses)
                .Must(addresses => addresses.Count <= MaxAddresses)
                .WithMessage($"A maximum of {MaxAddresses} addresses can be supplied.")
                .When(o => o.Addresses != null);

            RuleForEach(o => o.Addresses).SetValidator(new SignupAddressValidator());

            RuleFor(o => o.Theme!)
                .SetValidator(new SignupThemeValidator())
                .When(o => o.Theme != null);
        }

        private static bool BeAValidEmail(string? email)
        {
            const string emailValidatorExpression =
                @"\A(?:[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)\Z";

            return Regex.IsMatch(email!, emailValidatorExpression, RegexOptions.IgnoreCase, RegexTimeout);
        }

        private static bool BeAnAbsoluteHttpUrl(string? url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                   && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
        }

        private static bool BeAKnownTimeZone(string? timeZone)
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(timeZone!);
                return true;
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return false;
            }
        }
    }

    public class SignupAddressValidator : AbstractValidator<Address>
    {
        public SignupAddressValidator()
        {
            RuleFor(a => a.Name).MaximumLength(100).WithMessage("Maximum character limit 100 exceeded");
            RuleFor(a => a.AddressLine1).MaximumLength(200).WithMessage("Maximum character limit 200 exceeded");
            RuleFor(a => a.AddressLine2).MaximumLength(200).WithMessage("Maximum character limit 200 exceeded");
            RuleFor(a => a.City).MaximumLength(100).WithMessage("Maximum character limit 100 exceeded");
            RuleFor(a => a.State).MaximumLength(100).WithMessage("Maximum character limit 100 exceeded");
            RuleFor(a => a.PostalCode).MaximumLength(20).WithMessage("Maximum character limit 20 exceeded");
            RuleFor(a => a.Country).MaximumLength(100).WithMessage("Maximum character limit 100 exceeded");
        }
    }

    public class SignupThemeValidator : AbstractValidator<Theme>
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);
        private const string HexColorPattern = "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$";

        public SignupThemeValidator()
        {
            RuleFor(t => t.Name).MaximumLength(100).WithMessage("Maximum character limit 100 exceeded");
            RuleFor(t => t.PrimaryColor).Must(BeAHexColor).WithMessage("Primary color must be a hex color. E.g: #124091").When(t => !string.IsNullOrWhiteSpace(t.PrimaryColor));
            RuleFor(t => t.SecondaryColor).Must(BeAHexColor).WithMessage("Secondary color must be a hex color. E.g: #ffffff").When(t => !string.IsNullOrWhiteSpace(t.SecondaryColor));
            RuleFor(t => t.TertiaryColor).Must(BeAHexColor).WithMessage("Tertiary color must be a hex color. E.g: #000000").When(t => !string.IsNullOrWhiteSpace(t.TertiaryColor));
        }

        private static bool BeAHexColor(string? value)
        {
            return Regex.IsMatch(value!, HexColorPattern, RegexOptions.None, RegexTimeout);
        }
    }
}
