using Authentication.DomainService.OAuth;
using Authentication.DomainService.Shared.RequestModel;
using FluentAssertions;

namespace XUnitTest.Auth
{
    public sealed class OidcUiTemplateRequestValidatorTests
    {
        private readonly SaveOidcUiTemplateRequestValidator _validator = new();

        [Fact]
        public async Task CompleteDefaultTemplate_IsValid()
        {
            var result = await _validator.ValidateAsync(OidcUiTemplateTestData.ValidRequest());

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task OptionalNonThemeFields_CanAllBeNull()
        {
            var request = OidcUiTemplateTestData.ValidRequest();
            request.Branding!.LogoUrl = null;
            request.Pages!.Mfa!.ResendButton = null;
            request.Pages.AccountSelector!.Subheading = null;

            var result = await _validator.ValidateAsync(request);

            result.IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("http://assets.example.com/logo.svg")]
        [InlineData("https://assets.example.com/logo.svg")]
        public async Task LogoUrl_AcceptsAbsoluteHttpAndHttps(string value)
        {
            var request = OidcUiTemplateTestData.ValidRequest();
            request.Branding!.LogoUrl = value;

            (await _validator.ValidateAsync(request)).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData("")]
        [InlineData("logo.svg")]
        [InlineData("ftp://assets.example.com/logo.svg")]
        [InlineData("not a url")]
        public async Task LogoUrl_RejectsAnythingOtherThanAbsoluteHttpOrHttps(string value)
        {
            var request = OidcUiTemplateTestData.ValidRequest();
            request.Branding!.LogoUrl = value;

            var result = await _validator.ValidateAsync(request);

            result.Errors.Should().ContainSingle(e =>
                e.PropertyName == "Branding.LogoUrl" &&
                e.ErrorMessage == "must be an absolute http or https URL");
        }

        [Theory]
        [InlineData("#abc")]
        [InlineData("#A1b2C3")]
        public async Task RequiredColors_AcceptThreeOrSixDigitHex(string value)
        {
            var request = OidcUiTemplateTestData.ValidRequest();
            request.Theme!.Dark!.Primary = value;

            (await _validator.ValidateAsync(request)).IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task EveryRequiredColor_RejectsMissingOrNonHexValues()
        {
            var fields = RequiredColorFields();

            foreach (var (path, setValue) in fields)
            {
                foreach (var invalid in new string?[] { null, "", "blue", "rgba(0,0,0,1)", "#abcd" })
                {
                    var request = OidcUiTemplateTestData.ValidRequest();
                    setValue(request, invalid);

                    var result = await _validator.ValidateAsync(request);

                    result.Errors.Should().Contain(e =>
                        e.PropertyName == path &&
                        e.ErrorMessage == "must be a valid hex color (#RGB or #RRGGBB)",
                        $"{path} must reject '{invalid ?? "null"}'");
                }
            }
        }

        [Theory]
        [InlineData("#abc")]
        [InlineData("#A1b2C3")]
        [InlineData("rgba(0, 102, 178, 0.35)")]
        [InlineData("RGBA(255,0,1,1)")]
        [InlineData("rgba(1, 2, 3, .5)")]
        public async Task RequiredFlexibleColors_AcceptSupportedHexAndRgbaFormats(string value)
        {
            foreach (var (_, setValue) in RequiredFlexibleColorFields())
            {
                var request = OidcUiTemplateTestData.ValidRequest();
                setValue(request, value);

                (await _validator.ValidateAsync(request)).IsValid.Should().BeTrue();
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("blue")]
        [InlineData("#abcd")]
        [InlineData("rgb(0,0,0)")]
        [InlineData("rgba(256,0,0,1)")]
        [InlineData("rgba(0,0,0,1.1)")]
        [InlineData("rgba(0,0,0,-0.1)")]
        public async Task EveryRequiredFlexibleColor_RejectsMalformedOrOutOfRangeValues(string value)
        {
            foreach (var (path, setValue) in RequiredFlexibleColorFields())
            {
                var request = OidcUiTemplateTestData.ValidRequest();
                setValue(request, value);

                var result = await _validator.ValidateAsync(request);

                result.Errors.Should().Contain(e => e.PropertyName == path, $"{path} must reject '{value}'");
            }
        }

        [Fact]
        public async Task EveryRequiredFlexibleColor_RejectsNull()
        {
            foreach (var (path, setValue) in RequiredFlexibleColorFields())
            {
                var request = OidcUiTemplateTestData.ValidRequest();
                setValue(request, null);

                var result = await _validator.ValidateAsync(request);

                result.Errors.Should().Contain(e => e.PropertyName == path, $"{path} must be present");
            }
        }

        [Fact]
        public async Task EveryRequiredPageField_RejectsNullWhitespaceAndValuesOver200Characters()
        {
            foreach (var (path, setValue) in RequiredPageTextFields())
            {
                foreach (var invalid in new string?[] { null, " ", new('x', 201) })
                {
                    var request = OidcUiTemplateTestData.ValidRequest();
                    setValue(request, invalid);

                    var result = await _validator.ValidateAsync(request);

                    result.Errors.Should().Contain(e =>
                        e.PropertyName == path &&
                        e.ErrorMessage == "must be between 1 and 200 characters",
                        $"{path} must enforce its required 1-200 character contract");
                }
            }
        }

        [Fact]
        public async Task EveryRequiredPageField_AcceptsBoundaryLengths()
        {
            foreach (var (_, setValue) in RequiredPageTextFields())
            {
                foreach (var valid in new[] { "x", new string('x', 200) })
                {
                    var request = OidcUiTemplateTestData.ValidRequest();
                    setValue(request, valid);

                    (await _validator.ValidateAsync(request)).IsValid.Should().BeTrue();
                }
            }
        }

        [Fact]
        public async Task BrandName_EnforcesRequiredOneToEightyCharacterContract()
        {
            foreach (var invalid in new string?[] { null, " ", new('x', 81) })
            {
                var request = OidcUiTemplateTestData.ValidRequest();
                request.Branding!.BrandName = invalid;

                var result = await _validator.ValidateAsync(request);

                result.Errors.Should().ContainSingle(e =>
                    e.PropertyName == "Branding.BrandName" &&
                    e.ErrorMessage == "must be between 1 and 80 characters");
            }

            foreach (var valid in new[] { "x", new string('x', 80) })
            {
                var request = OidcUiTemplateTestData.ValidRequest();
                request.Branding!.BrandName = valid;
                (await _validator.ValidateAsync(request)).IsValid.Should().BeTrue();
            }
        }

        [Fact]
        public async Task OptionalPageFields_RejectWhitespaceAndValuesOver200ButAcceptBoundaries()
        {
            foreach (var (path, setValue) in OptionalPageTextFields())
            {
                foreach (var invalid in new[] { "", " ", new string('x', 201) })
                {
                    var request = OidcUiTemplateTestData.ValidRequest();
                    setValue(request, invalid);

                    var result = await _validator.ValidateAsync(request);
                    result.Errors.Should().Contain(e => e.PropertyName == path);
                }

                foreach (var valid in new string?[] { null, "x", new('x', 200) })
                {
                    var request = OidcUiTemplateTestData.ValidRequest();
                    setValue(request, valid);
                    (await _validator.ValidateAsync(request)).IsValid.Should().BeTrue();
                }
            }
        }

        [Fact]
        public async Task MissingRootAndPageObjects_ReportEveryMissingObject()
        {
            var request = new SaveOidcUiTemplateRequest();
            var rootResult = await _validator.ValidateAsync(request);
            rootResult.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("Branding", "Theme", "Pages");

            request = OidcUiTemplateTestData.ValidRequest();
            request.Theme!.Light = null;
            request.Theme.Dark = null;
            request.Pages!.Login = null;
            request.Pages.Signup = null;
            request.Pages.ForgotPassword = null;
            request.Pages.ResetPassword = null;
            request.Pages.Activation = null;
            request.Pages.Mfa = null;
            request.Pages.AccountSelector = null;
            request.Pages.Shared = null;

            var pageResult = await _validator.ValidateAsync(request);
            pageResult.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
                "Theme.Light", "Theme.Dark",
                "Pages.Login", "Pages.Signup", "Pages.ForgotPassword", "Pages.ResetPassword",
                "Pages.Activation", "Pages.Mfa", "Pages.AccountSelector", "Pages.Shared");
        }

        [Fact]
        public async Task InvalidPayload_ReportsAllInvalidFieldsAtOnce()
        {
            var request = OidcUiTemplateTestData.ValidRequest();
            request.Branding!.BrandName = null;
            request.Branding.LogoUrl = "relative.svg";
            request.Theme!.Dark!.Primary = "blue";
            request.Pages!.Login!.Heading = new string('x', 201);

            var result = await _validator.ValidateAsync(request);

            result.Errors.Select(e => e.PropertyName).Should().Contain(new[]
            {
                "Branding.BrandName", "Branding.LogoUrl", "Theme.Dark.Primary", "Pages.Login.Heading"
            });
        }

        private static IReadOnlyList<(string Path, Action<SaveOidcUiTemplateRequest, string?> SetValue)> RequiredColorFields() =>
        [
            ("Theme.Light.Primary", (r, v) => r.Theme!.Light!.Primary = v),
            ("Theme.Light.Secondary", (r, v) => r.Theme!.Light!.Secondary = v),
            ("Theme.Light.Background", (r, v) => r.Theme!.Light!.Background = v),
            ("Theme.Light.Surface", (r, v) => r.Theme!.Light!.Surface = v),
            ("Theme.Light.Text", (r, v) => r.Theme!.Light!.Text = v),
            ("Theme.Light.MutedText", (r, v) => r.Theme!.Light!.MutedText = v),
            ("Theme.Light.Success", (r, v) => r.Theme!.Light!.Success = v),
            ("Theme.Light.Danger", (r, v) => r.Theme!.Light!.Danger = v),
            ("Theme.Dark.Primary", (r, v) => r.Theme!.Dark!.Primary = v),
            ("Theme.Dark.Secondary", (r, v) => r.Theme!.Dark!.Secondary = v),
            ("Theme.Dark.Background", (r, v) => r.Theme!.Dark!.Background = v),
            ("Theme.Dark.Surface", (r, v) => r.Theme!.Dark!.Surface = v),
            ("Theme.Dark.Text", (r, v) => r.Theme!.Dark!.Text = v),
            ("Theme.Dark.MutedText", (r, v) => r.Theme!.Dark!.MutedText = v),
            ("Theme.Dark.Success", (r, v) => r.Theme!.Dark!.Success = v),
            ("Theme.Dark.Danger", (r, v) => r.Theme!.Dark!.Danger = v)
        ];

        private static IReadOnlyList<(string Path, Action<SaveOidcUiTemplateRequest, string?> SetValue)> RequiredFlexibleColorFields() =>
        [
            ("Theme.Light.Border", (r, v) => r.Theme!.Light!.Border = v),
            ("Theme.Light.BorderStrong", (r, v) => r.Theme!.Light!.BorderStrong = v),
            ("Theme.Light.AccentSoft", (r, v) => r.Theme!.Light!.AccentSoft = v),
            ("Theme.Dark.Border", (r, v) => r.Theme!.Dark!.Border = v),
            ("Theme.Dark.BorderStrong", (r, v) => r.Theme!.Dark!.BorderStrong = v),
            ("Theme.Dark.AccentSoft", (r, v) => r.Theme!.Dark!.AccentSoft = v)
        ];

        private static IReadOnlyList<(string Path, Action<SaveOidcUiTemplateRequest, string?> SetValue)> OptionalPageTextFields() =>
        [
            ("Pages.Mfa.ResendButton", (r, v) => r.Pages!.Mfa!.ResendButton = v),
            ("Pages.AccountSelector.Subheading", (r, v) => r.Pages!.AccountSelector!.Subheading = v)
        ];

        private static IReadOnlyList<(string Path, Action<SaveOidcUiTemplateRequest, string?> SetValue)> RequiredPageTextFields() =>
        [
            ("Pages.Login.Heading", (r, v) => r.Pages!.Login!.Heading = v),
            ("Pages.Login.EmailLabel", (r, v) => r.Pages!.Login!.EmailLabel = v),
            ("Pages.Login.PasswordLabel", (r, v) => r.Pages!.Login!.PasswordLabel = v),
            ("Pages.Login.ForgotPasswordLink", (r, v) => r.Pages!.Login!.ForgotPasswordLink = v),
            ("Pages.Login.SubmitButton", (r, v) => r.Pages!.Login!.SubmitButton = v),
            ("Pages.Login.SignupPrompt", (r, v) => r.Pages!.Login!.SignupPrompt = v),
            ("Pages.Login.SignupLink", (r, v) => r.Pages!.Login!.SignupLink = v),
            ("Pages.Login.ActivationErrorTitle", (r, v) => r.Pages!.Login!.ActivationErrorTitle = v),
            ("Pages.Login.ActivationErrorMessage", (r, v) => r.Pages!.Login!.ActivationErrorMessage = v),
            ("Pages.Login.ActivateAccountButton", (r, v) => r.Pages!.Login!.ActivateAccountButton = v),
            ("Pages.Login.BackToLoginButton", (r, v) => r.Pages!.Login!.BackToLoginButton = v),
            ("Pages.Signup.Heading", (r, v) => r.Pages!.Signup!.Heading = v),
            ("Pages.Signup.FirstNameLabel", (r, v) => r.Pages!.Signup!.FirstNameLabel = v),
            ("Pages.Signup.LastNameLabel", (r, v) => r.Pages!.Signup!.LastNameLabel = v),
            ("Pages.Signup.EmailLabel", (r, v) => r.Pages!.Signup!.EmailLabel = v),
            ("Pages.Signup.SubmitButton", (r, v) => r.Pages!.Signup!.SubmitButton = v),
            ("Pages.Signup.TermsPrefix", (r, v) => r.Pages!.Signup!.TermsPrefix = v),
            ("Pages.Signup.TermsLinkText", (r, v) => r.Pages!.Signup!.TermsLinkText = v),
            ("Pages.Signup.PrivacyLinkText", (r, v) => r.Pages!.Signup!.PrivacyLinkText = v),
            ("Pages.Signup.LoginPrompt", (r, v) => r.Pages!.Signup!.LoginPrompt = v),
            ("Pages.Signup.LoginLink", (r, v) => r.Pages!.Signup!.LoginLink = v),
            ("Pages.Signup.SuccessTitle", (r, v) => r.Pages!.Signup!.SuccessTitle = v),
            ("Pages.Signup.SuccessSubtitle", (r, v) => r.Pages!.Signup!.SuccessSubtitle = v),
            ("Pages.ForgotPassword.Heading", (r, v) => r.Pages!.ForgotPassword!.Heading = v),
            ("Pages.ForgotPassword.EmailLabel", (r, v) => r.Pages!.ForgotPassword!.EmailLabel = v),
            ("Pages.ForgotPassword.SubmitButton", (r, v) => r.Pages!.ForgotPassword!.SubmitButton = v),
            ("Pages.ResetPassword.Heading", (r, v) => r.Pages!.ResetPassword!.Heading = v),
            ("Pages.ResetPassword.PasswordLabel", (r, v) => r.Pages!.ResetPassword!.PasswordLabel = v),
            ("Pages.ResetPassword.ConfirmPasswordLabel", (r, v) => r.Pages!.ResetPassword!.ConfirmPasswordLabel = v),
            ("Pages.ResetPassword.LogoutFromDevicesLabel", (r, v) => r.Pages!.ResetPassword!.LogoutFromDevicesLabel = v),
            ("Pages.ResetPassword.SubmitButton", (r, v) => r.Pages!.ResetPassword!.SubmitButton = v),
            ("Pages.ResetPassword.SuccessTitle", (r, v) => r.Pages!.ResetPassword!.SuccessTitle = v),
            ("Pages.ResetPassword.SuccessSubtitle", (r, v) => r.Pages!.ResetPassword!.SuccessSubtitle = v),
            ("Pages.Activation.Heading", (r, v) => r.Pages!.Activation!.Heading = v),
            ("Pages.Activation.PasswordLabel", (r, v) => r.Pages!.Activation!.PasswordLabel = v),
            ("Pages.Activation.ConfirmPasswordLabel", (r, v) => r.Pages!.Activation!.ConfirmPasswordLabel = v),
            ("Pages.Activation.SubmitButton", (r, v) => r.Pages!.Activation!.SubmitButton = v),
            ("Pages.Activation.SuccessTitle", (r, v) => r.Pages!.Activation!.SuccessTitle = v),
            ("Pages.Activation.SuccessSubtitle", (r, v) => r.Pages!.Activation!.SuccessSubtitle = v),
            ("Pages.Mfa.Heading", (r, v) => r.Pages!.Mfa!.Heading = v),
            ("Pages.Mfa.SubmitButton", (r, v) => r.Pages!.Mfa!.SubmitButton = v),
            ("Pages.AccountSelector.Heading", (r, v) => r.Pages!.AccountSelector!.Heading = v),
            ("Pages.Shared.FooterText", (r, v) => r.Pages!.Shared!.FooterText = v)
        ];
    }
}
