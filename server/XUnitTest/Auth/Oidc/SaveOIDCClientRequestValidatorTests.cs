using Authentication.DomainService.OAuth;
using Authentication.DomainService.RequestModel;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace XUnitTest.Auth.Oidc
{
    public class SaveOIDCClientRequestValidatorTests
    {
        private readonly SaveOIDCClientRequestValidator _validator = new();

        [Fact]
        public void DeviceFlow_WithCodeResponseType_IsRejected()
        {
            var request = new SaveOIDCClientRequest
            {
                IsDeviceFlowClient = true,
                AllowedResponseTypes = new List<string> { "code" }
            };

            var result = _validator.TestValidate(request);
            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void DeviceFlow_WithDefaultResponseTypes_IsRejectedOnAllowedResponseTypesWithAClearMessage()
        {
            // AllowedResponseTypes defaults to ["code"], so a device client registered without
            // setting it used to fail with "The specified condition was not met for ''".
            var request = new SaveOIDCClientRequest { IsDeviceFlowClient = true };

            var result = _validator.TestValidate(request);

            result.Errors.Should().ContainSingle();
            result.ShouldHaveValidationErrorFor(x => x.AllowedResponseTypes)
                .WithErrorMessage(SaveOIDCClientRequestValidator.DeviceFlowResponseTypesMessage);
            result.Errors.Should().NotContain(e => string.IsNullOrEmpty(e.PropertyName));
        }

        [Fact]
        public void DeviceFlow_WithoutCodeResponseType_IsAccepted()
        {
            var request = new SaveOIDCClientRequest
            {
                IsDeviceFlowClient = true,
                AllowedResponseTypes = new List<string>()
            };

            var result = _validator.TestValidate(request);
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void NonDeviceFlow_WithCodeResponseType_IsAccepted()
        {
            var request = new SaveOIDCClientRequest
            {
                IsDeviceFlowClient = false,
                AllowedResponseTypes = new List<string> { "code" }
            };

            var result = _validator.TestValidate(request);
            result.IsValid.Should().BeTrue();
        }
    

        // ---------- redirect URI schemes (web or mobile app) ----------

        private const string RedirectUrisMessage =
            "Each redirect URI must be an http(s) URL or a mobile app URI whose scheme is a reverse domain name, such as com.example.app://callback.";
        private const string PostLogoutRedirectUrisMessage =
            "Each post-logout redirect URI must be an http(s) URL or a mobile app URI whose scheme is a reverse domain name, such as com.example.app://callback.";
        private const string ClientTypeMessage = "Mobile app redirect URIs require a public client.";
        private const string RequirePkceMessage = "Mobile app redirect URIs require PKCE.";
        private const string IdentityProviderMessage =
            "A client with mobile app redirect URIs cannot be registered as an identity provider.";

        private static SaveOIDCClientRequest PublicPkceClient(params string[] redirectUris) => new()
        {
            ClientType = "public",
            RequirePkce = true,
            RedirectUris = redirectUris.ToList()
        };

        [Fact]
        public void AppSchemeRedirect_OnPublicPkceClient_IsAccepted()
        {
            // H1 / Example 1
            var result = _validator.TestValidate(PublicPkceClient("com.klaxai.app://login/callback"));

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void MixedWebAndAppSchemeRedirects_OnPublicPkceClient_AreAccepted()
        {
            // H2
            var result = _validator.TestValidate(
                PublicPkceClient("https://app.example.com/cb", "com.example.app:/oauth2redirect"));

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void AppSchemePostLogoutRedirect_OnPublicPkceClient_IsAccepted()
        {
            // H4
            var request = PublicPkceClient("https://app.example.com/cb");
            request.PostLogoutRedirectUris = new List<string> { "com.example.app:/logout" };

            var result = _validator.TestValidate(request);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void AppSchemePublicClientType_IsCaseInsensitive()
        {
            var request = PublicPkceClient("com.example.app:/cb");
            request.ClientType = "PUBLIC";

            _validator.TestValidate(request).IsValid.Should().BeTrue();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("public")]
        [InlineData("confidential")]
        [InlineData("PUBLIC")]
        public void WebOnlyClient_AnyValidClientType_IsAcceptedAsBefore(string? clientType)
        {
            // H3: http(s)-only clients see no new rule, whatever their type, PKCE or federation.
            var request = new SaveOIDCClientRequest
            {
                ClientType = clientType,
                RequirePkce = false,
                RegisterAsIdentityProvider = true,
                RedirectUris = new List<string> { "https://app.example.com/cb", "http://localhost:3000/cb" },
                PostLogoutRedirectUris = new List<string> { "https://app.example.com/logout" }
            };

            var result = _validator.TestValidate(request);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public void WebOnlyClient_InvalidClientType_KeepsItsExistingMessageAndOnlyKey()
        {
            // H3: the existing ClientType rule still answers first, with its default text.
            var request = new SaveOIDCClientRequest
            {
                ClientType = "spa",
                RedirectUris = new List<string> { "https://app.example.com/cb" }
            };

            var result = _validator.TestValidate(request);

            result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(new[] { "ClientType" });
            result.Errors.Single().ErrorMessage.Should().Be("The specified condition was not met for 'Client Type'.");
        }

        [Fact]
        public void AppSchemeClient_InvalidClientType_FirstMessageIsStillTheExistingOne()
        {
            var result = _validator.TestValidate(new SaveOIDCClientRequest
            {
                ClientType = "spa",
                RedirectUris = new List<string> { "com.example.app:/cb" }
            });

            result.Errors.Where(e => e.PropertyName == "ClientType").Select(e => e.ErrorMessage).Should().Equal(
                "The specified condition was not met for 'Client Type'.", ClientTypeMessage);
        }

        [Theory]
        [InlineData("myapp://callback")]
        [InlineData("myapp:/callback")]
        public void RedirectWithDotlessScheme_IsRejectedWithRedirectUrisMessage(string uri)
        {
            // C1 / Example 2
            var result = _validator.TestValidate(PublicPkceClient(uri));

            result.ShouldHaveValidationErrorFor(x => x.RedirectUris).WithErrorMessage(RedirectUrisMessage);
            result.Errors.Should().ContainSingle();
        }

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("data:text/html,hi")]
        [InlineData("file:///etc/passwd")]
        [InlineData("vbscript:msgbox")]
        [InlineData("blob:https://example.com/uuid")]
        [InlineData("about:blank")]
        public void DangerousScheme_IsRejectedOnBothLists(string uri)
        {
            // C2
            var redirect = _validator.TestValidate(PublicPkceClient(uri));
            redirect.ShouldHaveValidationErrorFor(x => x.RedirectUris).WithErrorMessage(RedirectUrisMessage);

            var postLogout = PublicPkceClient("https://app.example.com/cb");
            postLogout.PostLogoutRedirectUris = new List<string> { uri };
            _validator.TestValidate(postLogout)
                .ShouldHaveValidationErrorFor(x => x.PostLogoutRedirectUris)
                .WithErrorMessage(PostLogoutRedirectUrisMessage);
        }

        [Theory]
        [InlineData("com.example.app://cb#frag")]
        [InlineData("intent://x#Intent;end")]
        public void AppSchemeWithFragment_IsRejectedWithRedirectUrisMessage(string uri)
        {
            // C3
            _validator.TestValidate(PublicPkceClient(uri))
                .ShouldHaveValidationErrorFor(x => x.RedirectUris)
                .WithErrorMessage(RedirectUrisMessage);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a uri")]
        public void BlankOrRelativeEntries_AreStillRejected(string uri)
        {
            var result = _validator.TestValidate(PublicPkceClient("https://app.example.com/cb", uri));

            result.ShouldHaveValidationErrorFor(x => x.RedirectUris).WithErrorMessage(RedirectUrisMessage);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("confidential")]
        public void AppSchemeRedirect_OnNonPublicClient_IsRejectedOnClientType(string? clientType)
        {
            // C4
            var request = PublicPkceClient("com.example.app:/cb");
            request.ClientType = clientType;

            var result = _validator.TestValidate(request);

            result.ShouldHaveValidationErrorFor(x => x.ClientType).WithErrorMessage(ClientTypeMessage);
        }

        [Fact]
        public void AppSchemeRedirect_WithoutClientType_DefaultRequest_IsRejectedOnClientType()
        {
            // Example 3: no clientType, RequirePkce defaults to true.
            var result = _validator.TestValidate(new SaveOIDCClientRequest
            {
                RedirectUris = new List<string> { "com.klaxai.app://login/callback" }
            });

            result.Errors.Should().ContainSingle();
            result.ShouldHaveValidationErrorFor("ClientType").WithErrorMessage(ClientTypeMessage);
        }

        [Fact]
        public void AppSchemePostLogoutOnly_OnConfidentialClient_IsRejectedOnClientType()
        {
            var request = new SaveOIDCClientRequest
            {
                ClientType = "confidential",
                RedirectUris = new List<string> { "https://app.example.com/cb" },
                PostLogoutRedirectUris = new List<string> { "com.example.app:/logout" }
            };

            _validator.TestValidate(request)
                .ShouldHaveValidationErrorFor(x => x.ClientType)
                .WithErrorMessage(ClientTypeMessage);
        }

        [Fact]
        public void AppSchemeRedirect_WithoutPkce_IsRejectedOnRequirePkce()
        {
            // C5
            var request = PublicPkceClient("com.example.app:/cb");
            request.RequirePkce = false;

            var result = _validator.TestValidate(request);

            result.Errors.Should().ContainSingle();
            result.ShouldHaveValidationErrorFor("RequirePkce").WithErrorMessage(RequirePkceMessage);
        }

        [Fact]
        public void AppSchemeRedirect_RegisteredAsIdentityProvider_IsRejected()
        {
            // C6
            var request = PublicPkceClient("com.example.app:/cb");
            request.RegisterAsIdentityProvider = true;

            var result = _validator.TestValidate(request);

            result.Errors.Should().ContainSingle();
            result.ShouldHaveValidationErrorFor("RegisterAsIdentityProvider").WithErrorMessage(IdentityProviderMessage);
        }

        [Fact]
        public void AppSchemeRedirect_AllThreeRulesBroken_ReportsEachKey()
        {
            var request = new SaveOIDCClientRequest
            {
                ClientType = "confidential",
                RequirePkce = false,
                RegisterAsIdentityProvider = true,
                RedirectUris = new List<string> { "com.example.app:/cb" }
            };

            var result = _validator.TestValidate(request);

            result.Errors.Select(e => e.PropertyName).Should()
                .BeEquivalentTo(new[] { "ClientType", "RequirePkce", "RegisterAsIdentityProvider" });
        }

        [Fact]
        public void DeviceFlowClient_IsExemptFromAppSchemeClientRules()
        {
            var request = new SaveOIDCClientRequest
            {
                IsDeviceFlowClient = true,
                AllowedResponseTypes = new List<string>(),
                ClientType = "confidential",
                RequirePkce = false,
                RegisterAsIdentityProvider = true,
                RedirectUris = new List<string> { "com.example.app:/cb" }
            };

            var result = _validator.TestValidate(request);

            result.IsValid.Should().BeTrue();
        }
    }
}
