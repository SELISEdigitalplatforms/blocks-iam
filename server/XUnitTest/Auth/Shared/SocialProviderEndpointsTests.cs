using Authentication.DomainService.Services;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;

namespace XUnitTest.Auth.Shared
{
    /// <summary>Google and Microsoft social entries keep the built-in endpoints; Microsoft may name its Entra tenant.</summary>
    public class SocialProviderEndpointsTests
    {
        private static IdentityProvider Entry(string provider, string type = "social", string? wellKnownUrl = null) => new()
        {
            Provider = provider, ProviderType = type, ClientId = "cid", ClientSecret = "s",
            TokenEndpointAuthMethod = "client_secret_post", WellKnownUrl = wellKnownUrl
        };

        [Theory]
        [InlineData("https://login.microsoftonline.com/8b1d2c3e-0000-4e72-9a7f-000000000001/v2.0/.well-known/openid-configuration", "8b1d2c3e-0000-4e72-9a7f-000000000001")]
        [InlineData("https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0/.well-known/openid-configuration", "contoso.onmicrosoft.com")]
        [InlineData("https://LOGIN.microsoftonline.com/organizations/v2.0/.well-known/openid-configuration/", "organizations")]
        [InlineData("https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration", "common")]
        public void TryReadMicrosoftTenant_ReadsTheTenantSegment(string url, string expected)
        {
            SocialProviderEndpoints.TryReadMicrosoftTenant(url, out var tenant).Should().BeTrue();
            tenant.Should().Be(expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a url")]
        [InlineData("http://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration")]
        [InlineData("https://login.microsoftonline.com:8443/common/v2.0/.well-known/openid-configuration")]
        [InlineData("https://login.microsoftonline.com.evil.example/common/v2.0/.well-known/openid-configuration")]
        [InlineData("https://login.microsoftonline.com/v2.0/.well-known/openid-configuration")]
        [InlineData("https://login.microsoftonline.com/common/.well-known/openid-configuration")]
        [InlineData("https://login.microsoftonline.com/a/b/v2.0/.well-known/openid-configuration")]
        [InlineData("https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration?x=1")]
        [InlineData("https://login.microsoftonline.com/con_toso/v2.0/.well-known/openid-configuration")]
        [InlineData("https://accounts.google.com/.well-known/openid-configuration")]
        public void TryReadMicrosoftTenant_RefusesAnythingElse(string? url)
        {
            SocialProviderEndpoints.TryReadMicrosoftTenant(url, out var tenant).Should().BeFalse();
            tenant.Should().BeEmpty();
        }

        [Fact]
        public void Apply_Microsoft_UsesTheNamedTenant_AndGraphForTheProfile()
        {
            var entry = Entry("microsoft", wellKnownUrl: "https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0/.well-known/openid-configuration");

            SocialProviderEndpoints.Apply(entry);

            entry.WellKnownUrl.Should().Be("https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0/.well-known/openid-configuration");
            entry.AuthorizationUrl.Should().Be("https://login.microsoftonline.com/contoso.onmicrosoft.com/oauth2/v2.0/authorize");
            entry.TokenUrl.Should().Be("https://login.microsoftonline.com/contoso.onmicrosoft.com/oauth2/v2.0/token");
            entry.UserInfoUrl.Should().Be("https://graph.microsoft.com/v1.0/me");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("https://idp.example.com/.well-known/openid-configuration")]
        public void Apply_Microsoft_WithoutATenantUrl_UsesCommon(string? wellKnownUrl)
        {
            var entry = Entry("Microsoft", wellKnownUrl: wellKnownUrl);

            SocialProviderEndpoints.Apply(entry);

            entry.WellKnownUrl.Should().Be("https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration");
            entry.AuthorizationUrl.Should().Be("https://login.microsoftonline.com/common/oauth2/v2.0/authorize");
            entry.TokenUrl.Should().Be("https://login.microsoftonline.com/common/oauth2/v2.0/token");
        }

        [Fact]
        public void Apply_Google_IgnoresAnyDiscoveryUrl()
        {
            var entry = Entry("google", wellKnownUrl: "https://idp.example.com/.well-known/openid-configuration");

            SocialProviderEndpoints.Apply(entry);

            entry.WellKnownUrl.Should().Be("https://accounts.google.com/.well-known/openid-configuration");
            entry.AuthorizationUrl.Should().Be("https://accounts.google.com/o/oauth2/v2/auth");
            entry.TokenUrl.Should().Be("https://oauth2.googleapis.com/token");
            entry.UserInfoUrl.Should().Be("https://www.googleapis.com/oauth2/v1/userinfo?access_token={0}");
        }

        [Theory]
        [InlineData("social", "google", true)]
        [InlineData("SOCIAL", "Microsoft", true)]
        [InlineData("social", "google-staff", false)]
        [InlineData("social", "apple", false)]
        [InlineData("byos", "google", false)]
        [InlineData("blocks-oidc", "microsoft", false)]
        public void IsBuiltIn_OnlyGoogleAndMicrosoftOfTheSocialType(string type, string provider, bool expected)
        {
            SocialProviderEndpoints.IsBuiltIn(type, provider).Should().Be(expected);
        }

        [Fact]
        public void OldestFirst_OrdersByCreationThenId()
        {
            var at = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
            var newer = Entry("google"); newer.ItemId = "a"; newer.CreatedDate = at.AddDays(1);
            var older = Entry("google"); older.ItemId = "z"; older.CreatedDate = at;
            var sameTimeB = Entry("microsoft"); sameTimeB.ItemId = "b"; sameTimeB.CreatedDate = at.AddDays(2);
            var sameTimeA = Entry("microsoft"); sameTimeA.ItemId = "a2"; sameTimeA.CreatedDate = at.AddDays(2);

            AuthenticationRepository.OldestFirst([newer, sameTimeB, older, sameTimeA])
                .Select(p => p.ItemId).Should().Equal("z", "a", "a2", "b");
        }
    }
}
