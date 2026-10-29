using Authentication.DomainService.Authentication.LoginPolicies;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;

namespace XUnitTest.Auth.LoginPolicies
{
    public class LoginPolicyRulesTests
    {
        private const string Password = LoginMethods.Password;
        private const string Social = LoginMethods.Social;

        private static IdentityProvider Provider(string id, bool active = true) =>
            new() { ItemId = id, Provider = "google", ProviderType = "social", IsActive = active, ClientId = id, ClientSecret = "s", TokenEndpointAuthMethod = "client_secret_post" };

        private static OidcClientLoginPolicy Client(string method = OidcClientLoginMethods.Default, params string[] selected) => new()
        {
            Method = method,
            Providers = selected.Length > 0 ? OidcClientProviderModes.Selected : OidcClientProviderModes.Default,
            SelectedProviderIds = selected.ToList()
        };

        [Theory]
        [InlineData(LoginChannel.Embedded, Password)]
        [InlineData(LoginChannel.Embedded, Social)]
        [InlineData(LoginChannel.Oidc, Password)]
        [InlineData(LoginChannel.Oidc, Social)]
        public void NoPolicyAnywhere_AllowsEverything(LoginChannel channel, string method)
        {
            LoginPolicyRules.Check(null, channel, method, null, Provider("p1")).IsAllowed.Should().BeTrue();
        }

        [Fact]
        public void EmbeddedOff_RefusesEmbedded_ButNotOidc()
        {
            var tenant = new TenantLoginPolicy { AllowEmbedded = false };

            var embedded = LoginPolicyRules.Check(tenant, LoginChannel.Embedded, Password, null, null);
            embedded.IsAllowed.Should().BeFalse();
            embedded.Message.Should().Contain("turned off for this project");
            LoginPolicyRules.Check(tenant, LoginChannel.Oidc, Password, null, null).IsAllowed.Should().BeTrue();
        }

        [Fact]
        public void OidcOff_RefusesOidc_ButNotEmbedded()
        {
            var tenant = new TenantLoginPolicy { AllowOidc = false };

            LoginPolicyRules.Check(tenant, LoginChannel.Oidc, Social, null, null).IsAllowed.Should().BeFalse();
            LoginPolicyRules.CheckChannel(tenant, LoginChannel.Oidc).IsAllowed.Should().BeFalse();
            LoginPolicyRules.Check(tenant, LoginChannel.Embedded, Social, null, null).IsAllowed.Should().BeTrue();
        }

        [Theory]
        [InlineData(false, true, Password, false)]
        [InlineData(false, true, Social, true)]
        [InlineData(true, false, Password, true)]
        [InlineData(true, false, Social, false)]
        public void TenantMethodSwitches_ApplyOnBothChannels(bool password, bool social, string method, bool expected)
        {
            var tenant = new TenantLoginPolicy { AllowPassword = password, AllowSocial = social };

            LoginPolicyRules.Check(tenant, LoginChannel.Embedded, method, null, null).IsAllowed.Should().Be(expected);
            LoginPolicyRules.Check(tenant, LoginChannel.Oidc, method, null, null).IsAllowed.Should().Be(expected);
        }

        [Theory]
        [InlineData(OidcClientLoginMethods.Default, Password, true)]
        [InlineData(OidcClientLoginMethods.Default, Social, true)]
        [InlineData(OidcClientLoginMethods.Password, Password, true)]
        [InlineData(OidcClientLoginMethods.Password, Social, false)]
        [InlineData(OidcClientLoginMethods.Social, Password, false)]
        [InlineData(OidcClientLoginMethods.Social, Social, true)]
        [InlineData("Unknown", Password, true)]
        public void ClientMethod_NarrowsOidcOnly(string clientMethod, string method, bool expected)
        {
            LoginPolicyRules.Check(null, LoginChannel.Oidc, method, Client(clientMethod), null).IsAllowed.Should().Be(expected);
            LoginPolicyRules.Check(null, LoginChannel.Embedded, method, Client(clientMethod), null).IsAllowed.Should().BeTrue();
        }

        [Fact]
        public void ClientCannotWiden_TheTenant()
        {
            var tenant = new TenantLoginPolicy { AllowSocial = false };

            LoginPolicyRules.Check(tenant, LoginChannel.Oidc, Social, Client(OidcClientLoginMethods.Social), null).IsAllowed.Should().BeFalse();
        }

        [Fact]
        public void SelectedProviders_OnlyThoseForOidc_AllEnabledForEmbedded()
        {
            var client = Client(OidcClientLoginMethods.Default, "p1");

            LoginPolicyRules.Check(null, LoginChannel.Oidc, Social, client, Provider("p1")).IsAllowed.Should().BeTrue();
            LoginPolicyRules.Check(null, LoginChannel.Oidc, Social, client, Provider("p2")).IsAllowed.Should().BeFalse();
            LoginPolicyRules.Check(null, LoginChannel.Embedded, Social, client, Provider("p2")).IsAllowed.Should().BeTrue();
        }

        [Fact]
        public void DisabledProvider_IsRefused_EvenWhenSelected()
        {
            var client = Client(OidcClientLoginMethods.Default, "p1");

            LoginPolicyRules.Check(null, LoginChannel.Oidc, Social, client, Provider("p1", active: false)).IsAllowed.Should().BeFalse();
            LoginPolicyRules.Check(null, LoginChannel.Embedded, Social, null, Provider("p1", active: false)).IsAllowed.Should().BeFalse();
        }

        [Fact]
        public void Options_FollowTenantClientAndProviders()
        {
            var providers = new[] { Provider("p1"), Provider("p2"), Provider("p3", active: false) };

            var all = LoginPolicyRules.Options(null, LoginChannel.Oidc, null, providers);
            all.Password.Should().BeTrue();
            all.Social.Should().BeTrue();
            all.Providers.Select(p => p.ItemId).Should().Equal("p1", "p2");

            var selected = LoginPolicyRules.Options(null, LoginChannel.Oidc, Client(OidcClientLoginMethods.Default, "p2", "p3"), providers);
            selected.Providers.Select(p => p.ItemId).Should().Equal("p2");

            var passwordOnly = LoginPolicyRules.Options(null, LoginChannel.Oidc, Client(OidcClientLoginMethods.Password), providers);
            passwordOnly.Password.Should().BeTrue();
            passwordOnly.Social.Should().BeFalse();
            passwordOnly.Providers.Should().BeEmpty();

            var socialOff = LoginPolicyRules.Options(new TenantLoginPolicy { AllowSocial = false }, LoginChannel.Embedded, null, providers);
            socialOff.Social.Should().BeFalse();
            socialOff.Providers.Should().BeEmpty();
        }

        [Fact]
        public void Options_SocialWithoutAnyAllowedProvider_IsHidden()
        {
            var options = LoginPolicyRules.Options(null, LoginChannel.Oidc, Client(OidcClientLoginMethods.Social, "gone"), [Provider("p1")]);

            options.Social.Should().BeFalse();
            options.Password.Should().BeFalse();
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData(Password, true)]
        [InlineData(Social, false)]
        public void SessionAccount_FollowsClientMethod_OldAccountsNotBlocked(string? accountMethod, bool expected)
        {
            LoginPolicyRules.AllowsSessionAccount(null, Client(OidcClientLoginMethods.Password), accountMethod).Should().Be(expected);
        }
    }
}
