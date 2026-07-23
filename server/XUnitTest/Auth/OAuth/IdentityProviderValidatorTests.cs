using System.Net;
using System.Net.Http;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Shared.RequestModel;
using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using Moq.Protected;

namespace XUnitTest.Auth.OAuth
{
    public class IdentityProviderValidatorTests
    {
        private static SaveIdentityProviderRequestValidator CreateSaveValidator()
        {
            // WellKnownUrl is left empty in these tests, so the HTTP metadata rule never fires.
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
            return new SaveIdentityProviderRequestValidator(factory.Object);
        }

        private static SaveIdentityProviderRequest ValidSaveRequest() => new()
        {
            Provider = "google",
            ProviderType = "social",
            Protocol = "oidc",
            ClientId = "client-1"
        };

        [Fact]
        public void Save_ValidMinimalRequest_Passes()
        {
            var result = CreateSaveValidator().TestValidate(ValidSaveRequest());
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Save_MissingRequiredFields_Fails()
        {
            var result = CreateSaveValidator().TestValidate(new SaveIdentityProviderRequest());
            result.ShouldHaveValidationErrorFor(x => x.Provider);
            result.ShouldHaveValidationErrorFor(x => x.ProviderType);
            result.ShouldHaveValidationErrorFor(x => x.Protocol);
            result.ShouldHaveValidationErrorFor(x => x.ClientId);
        }

        [Theory]
        [InlineData("social")]
        [InlineData("byos")]
        [InlineData("blocks-oidc")]
        public void Save_AcceptsKnownProviderTypes(string providerType)
        {
            var req = ValidSaveRequest();
            req.ProviderType = providerType;
            var result = CreateSaveValidator().TestValidate(req);
            result.ShouldNotHaveValidationErrorFor(x => x.ProviderType);
        }

        [Fact]
        public void Save_RejectsUnknownProviderType()
        {
            var req = ValidSaveRequest();
            req.ProviderType = "carrier-pigeon";
            var result = CreateSaveValidator().TestValidate(req);
            result.ShouldHaveValidationErrorFor(x => x.ProviderType);
        }

        [Theory]
        [InlineData("oidc")]
        [InlineData("oauth2")]
        [InlineData("saml")]
        [InlineData("ldap")]
        public void Save_AcceptsKnownProtocols(string protocol)
        {
            var req = ValidSaveRequest();
            req.Protocol = protocol;
            var result = CreateSaveValidator().TestValidate(req);
            result.ShouldNotHaveValidationErrorFor(x => x.Protocol);
        }

        [Fact]
        public void Save_RejectsInvalidUrls()
        {
            var req = ValidSaveRequest();
            req.AuthorizationUrl = "not-a-url";
            req.TokenUrl = "ftp://insecure";
            var result = CreateSaveValidator().TestValidate(req);
            result.ShouldHaveValidationErrorFor(x => x.AuthorizationUrl);
            result.ShouldHaveValidationErrorFor(x => x.TokenUrl);
        }

        [Fact]
        public void Save_AcceptsHttpsUrls()
        {
            var req = ValidSaveRequest();
            req.AuthorizationUrl = "https://idp.example.com/authorize";
            req.TokenUrl = "https://idp.example.com/token";
            req.UserInfoUrl = "https://idp.example.com/userinfo";
            req.JwksUri = "https://idp.example.com/jwks";
            var result = CreateSaveValidator().TestValidate(req);
            result.ShouldNotHaveValidationErrorFor(x => x.AuthorizationUrl);
            result.ShouldNotHaveValidationErrorFor(x => x.TokenUrl);
            result.ShouldNotHaveValidationErrorFor(x => x.UserInfoUrl);
            result.ShouldNotHaveValidationErrorFor(x => x.JwksUri);
        }

        [Fact]
        public void Save_RejectsRedirectUrisWithInvalidEntry()
        {
            var req = ValidSaveRequest();
            req.RedirectUris = new List<string> { "https://ok.com", "  " };
            var result = CreateSaveValidator().TestValidate(req);
            result.ShouldHaveValidationErrorFor(x => x.RedirectUris);
        }

        [Fact]
        public void Save_RejectsEmptyGrantTypeEntry()
        {
            var req = ValidSaveRequest();
            req.GrantTypes = new List<string> { "authorization_code", "" };
            var result = CreateSaveValidator().TestValidate(req);
            result.ShouldHaveValidationErrorFor(x => x.GrantTypes);
        }

        [Fact]
        public async Task Save_WithWellKnownUrl_InvalidMetadata_Fails()
        {
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));
            var validator = new SaveIdentityProviderRequestValidator(factory.Object);

            var req = ValidSaveRequest();
            req.WellKnownUrl = "https://idp.example.com/.well-known/openid-configuration";

            var result = await validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.WellKnownUrl);
        }

        [Fact]
        public async Task Save_WithWellKnownUrl_ValidMetadata_Passes()
        {
            var json = "{\"authorization_endpoint\":\"https://idp/auth\",\"token_endpoint\":\"https://idp/token\"}";
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                });
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));
            var validator = new SaveIdentityProviderRequestValidator(factory.Object);

            var req = ValidSaveRequest();
            req.WellKnownUrl = "https://idp.example.com/.well-known/openid-configuration";

            var result = await validator.TestValidateAsync(req);
            result.ShouldNotHaveValidationErrorFor(x => x.WellKnownUrl);
        }

        // ---- Update validator ----

        private readonly UpdateIdentityProviderRequestValidator _updateValidator = new();

        [Fact]
        public void Update_EmptyRequest_Passes_BecauseAllRulesAreConditional()
        {
            var result = _updateValidator.TestValidate(new UpdateIdentityProviderRequest());
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void Update_ProvidedButEmptyProvider_Fails()
        {
            var result = _updateValidator.TestValidate(new UpdateIdentityProviderRequest { Provider = "" });
            result.ShouldHaveValidationErrorFor(x => x.Provider);
        }

        [Fact]
        public void Update_InvalidProviderTypeAndProtocol_Fails()
        {
            var result = _updateValidator.TestValidate(new UpdateIdentityProviderRequest
            {
                ProviderType = "unknown",
                Protocol = "carrier"
            });
            result.ShouldHaveValidationErrorFor(x => x.ProviderType);
            result.ShouldHaveValidationErrorFor(x => x.Protocol);
        }

        [Fact]
        public void Update_ValidValues_Passes()
        {
            var result = _updateValidator.TestValidate(new UpdateIdentityProviderRequest
            {
                Provider = "google",
                ProviderType = "social",
                Protocol = "oidc",
                ClientId = "c1",
                WellKnownUrl = "https://idp/.well-known",
                RedirectUris = new List<string> { "https://ok.com" },
                GrantTypes = new List<string> { "authorization_code" },
                InitialRoles = new List<string> { "user" },
                InitialPermissions = new List<string> { "read" }
            });
            result.IsValid.Should().BeTrue();
        }
    }
}
