using System.Reflection;
using Authentication.DomainService.OAuth.RequestModel;
using FluentAssertions;

namespace XUnitTest.Auth.Oidc
{
    /// <summary>
    /// Unit tests for <c>Authentication.DomainService.Authentication.OidcAuthRequestValidator</c>.
    /// The class is <c>internal static</c> (no InternalsVisibleTo to the test project), so its
    /// <c>Validate</c> and <c>ValidatePkceFormat</c> methods are invoked through reflection.
    /// </summary>
    public class OidcAuthRequestValidatorTests
    {
        // 43-char BASE64URL string (minimum valid length per RFC 7636).
        private const string ValidChallenge = "0123456789012345678901234567890123456789012";

        private static readonly Type ValidatorType =
            typeof(AuthorizeRequest).Assembly.GetType(
                "Authentication.DomainService.Authentication.OidcAuthRequestValidator", throwOnError: true)!;

        private static AuthorizeValidationResult Validate(AuthorizeRequest request, bool isDeviceFlowClient = false)
        {
            var method = ValidatorType.GetMethod("Validate", BindingFlags.Public | BindingFlags.Static)!;
            return (AuthorizeValidationResult)method.Invoke(null, new object[] { request, isDeviceFlowClient })!;
        }

        private static bool ValidatePkceFormat(string challenge)
        {
            var method = ValidatorType.GetMethod("ValidatePkceFormat", BindingFlags.Public | BindingFlags.Static)!;
            return (bool)method.Invoke(null, new object[] { challenge })!;
        }

        private static AuthorizeRequest ValidRequest() => new()
        {
            ClientId = "client-1",
            ResponseType = "code",
            RedirectUri = "https://app.test/callback",
            Scope = "openid profile email"
        };

        // ---------- Validate: happy paths ----------

        [Fact]
        public void Validate_ValidRequestWithoutPkce_IsValid()
        {
            var result = Validate(ValidRequest());

            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public void Validate_ValidRequestWithPkce_IsValid()
        {
            var request = ValidRequest();
            request.CodeChallenge = ValidChallenge;
            request.CodeChallengeMethod = "S256";

            var result = Validate(request);

            result.IsValid.Should().BeTrue();
            result.Errors.Should().BeEmpty();
        }

        // ---------- Validate: individual invalid branches ----------

        [Fact]
        public void Validate_MissingClientId_ReportsError()
        {
            var request = ValidRequest();
            request.ClientId = null;

            var result = Validate(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain("client_id is required");
        }

        [Fact]
        public void Validate_MissingResponseType_ReportsError()
        {
            var request = ValidRequest();
            request.ResponseType = "";

            var result = Validate(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain("response_type is required");
        }

        [Fact]
        public void Validate_UnsupportedResponseType_ReportsError()
        {
            var request = ValidRequest();
            request.ResponseType = "token";

            var result = Validate(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain("response_type must be 'code'");
        }

        [Fact]
        public void Validate_MissingRedirectUri_ReportsError()
        {
            var request = ValidRequest();
            request.RedirectUri = "   ";

            var result = Validate(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain("redirect_uri is required");
        }

        [Fact]
        public void Validate_MissingScope_ReportsError()
        {
            var request = ValidRequest();
            request.Scope = null;

            var result = Validate(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain("scope is required");
        }

        [Fact]
        public void Validate_ScopeWithoutOpenid_ReportsError()
        {
            var request = ValidRequest();
            request.Scope = "profile email";

            var result = Validate(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain("scope must include 'openid'");
        }

        [Fact]
        public void Validate_InvalidCodeChallengeFormat_ReportsError()
        {
            var request = ValidRequest();
            request.CodeChallenge = "too-short";
            request.CodeChallengeMethod = "S256";

            var result = Validate(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain("code_challenge has invalid format");
        }

        [Fact]
        public void Validate_CodeChallengeWithoutMethod_ReportsError()
        {
            var request = ValidRequest();
            request.CodeChallenge = ValidChallenge;
            request.CodeChallengeMethod = null;

            var result = Validate(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain("code_challenge_method is required when code_challenge is provided");
        }

        [Fact]
        public void Validate_UnsupportedCodeChallengeMethod_ReportsError()
        {
            var request = ValidRequest();
            request.CodeChallenge = ValidChallenge;
            request.CodeChallengeMethod = "plain";

            var result = Validate(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.Contains("code_challenge_method must be"));
        }

        [Fact]
        public void Validate_AllInvalid_AggregatesErrors()
        {
            var request = new AuthorizeRequest
            {
                ClientId = null,
                ResponseType = null,
                RedirectUri = null,
                Scope = null
            };

            var result = Validate(request);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().HaveCount(4);
        }

        // ---------- ValidatePkceFormat ----------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ValidatePkceFormat_NullOrEmpty_ReturnsFalse(string? challenge)
        {
            ValidatePkceFormat(challenge!).Should().BeFalse();
        }

        [Fact]
        public void ValidatePkceFormat_TooShort_ReturnsFalse()
        {
            ValidatePkceFormat(new string('A', 42)).Should().BeFalse();
        }

        [Fact]
        public void ValidatePkceFormat_TooLong_ReturnsFalse()
        {
            ValidatePkceFormat(new string('A', 129)).Should().BeFalse();
        }

        [Fact]
        public void ValidatePkceFormat_InvalidCharacter_ReturnsFalse()
        {
            // 43 chars but contains '+', which is not in the BASE64URL alphabet.
            ValidatePkceFormat(new string('A', 42) + "+").Should().BeFalse();
        }

        [Theory]
        [InlineData(43)]
        [InlineData(128)]
        public void ValidatePkceFormat_ValidLengthAndAlphabet_ReturnsTrue(int length)
        {
            ValidatePkceFormat(new string('a', length)).Should().BeTrue();
        }
    }
}
