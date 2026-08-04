using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.Shared.Services;
using FluentAssertions;

namespace XUnitTest.Auth.Oidc
{
    public class OidcClientValidatorTests
    {
        private static OidcClientRegistration Build(bool isDeviceFlow, bool isActive = true, string? tokenEndpointAuthMethod = null)
        {
            return new OidcClientRegistration
            {
                ItemId = "test",
                ClientId = "test",
                ClientName = "Test",
                IsActive = isActive,
                IsDeviceFlowClient = isDeviceFlow,
                TokenEndpointAuthMethod = tokenEndpointAuthMethod,
                AllowedScopes = new List<string> { "openid", "profile", "email", "offline_access" }
            };
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void InactiveClient_IsDeniedForAllGrants(bool isDeviceFlow)
        {
            var client = Build(isDeviceFlow, isActive: false);

            OidcClientValidator.IsGrantAllowed(client, GrantTypes.DeviceCode).Should().BeFalse();
            OidcClientValidator.IsGrantAllowed(client, GrantTypes.AuthCode).Should().BeFalse();
            OidcClientValidator.IsGrantAllowed(client, GrantTypes.RefreshToken).Should().BeFalse();
            OidcClientValidator.IsGrantAllowed(client, GrantTypes.ClientCredential).Should().BeFalse();
        }

        [Fact]
        public void DeviceFlowClient_AllowsDeviceCodeAndRefreshToken_ButNotAuthCodeOrClientCredential()
        {
            var client = Build(isDeviceFlow: true);

            // RFC 8628 device clients mint their initial token via device_code, but must still be
            // able to refresh like any other client — refresh_token is not authorization_code.
            OidcClientValidator.IsGrantAllowed(client, GrantTypes.DeviceCode).Should().BeTrue();
            OidcClientValidator.IsGrantAllowed(client, GrantTypes.AuthCode).Should().BeFalse();
            OidcClientValidator.IsGrantAllowed(client, GrantTypes.RefreshToken).Should().BeTrue();
            OidcClientValidator.IsGrantAllowed(client, GrantTypes.ClientCredential).Should().BeFalse();
        }

        [Fact]
        public void StandardClient_AllowsTraditionalGrants_AndRejectsDeviceCode()
        {
            var client = Build(isDeviceFlow: false, tokenEndpointAuthMethod: "client_secret_post");

            OidcClientValidator.IsGrantAllowed(client, GrantTypes.AuthCode).Should().BeTrue();
            OidcClientValidator.IsGrantAllowed(client, GrantTypes.RefreshToken).Should().BeTrue();
            OidcClientValidator.IsGrantAllowed(client, GrantTypes.ClientCredential).Should().BeTrue();
            OidcClientValidator.IsGrantAllowed(client, GrantTypes.DeviceCode).Should().BeFalse();
        }

        [Fact]
        public void ClientCredentials_RequiresNonNoneAuthMethod()
        {
            var none = Build(isDeviceFlow: false, tokenEndpointAuthMethod: "none");
            OidcClientValidator.IsGrantAllowed(none, GrantTypes.ClientCredential).Should().BeFalse();

            var basic = Build(isDeviceFlow: false, tokenEndpointAuthMethod: "client_secret_basic");
            OidcClientValidator.IsGrantAllowed(basic, GrantTypes.ClientCredential).Should().BeTrue();
        }

        [Fact]
        public void ValidateScopes_ReturnsIntersectionOfAllowedAndSupported()
        {
            var client = Build(isDeviceFlow: true);
            client.AllowedScopes = new List<string> { "openid", "profile", "custom" };

            var scopes = OidcClientValidator.ValidateScopes(client, "openid profile email", ScopeConstants.Supported);
            scopes.Should().BeEquivalentTo(new[] { "openid", "profile" });
        }

        [Fact]
        public void ValidateScopes_NoRequestedScope_ReturnsAllAllowedThatAreSupported()
        {
            var client = Build(isDeviceFlow: true);
            client.AllowedScopes = new List<string> { "openid", "profile", "custom" };

            var scopes = OidcClientValidator.ValidateScopes(client, null, ScopeConstants.Supported);
            scopes.Should().BeEquivalentTo(new[] { "openid", "profile" });
        }
    }
}