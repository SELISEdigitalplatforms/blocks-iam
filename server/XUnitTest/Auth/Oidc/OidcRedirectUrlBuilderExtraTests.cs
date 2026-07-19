using System.Net;
using System.Text;
using Authentication.DomainService.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Auth.Oidc
{
    public class OidcRedirectUrlBuilderExtraTests
    {
        [Fact]
        public void BuildRedirectUri_AppendsQuery_AndSkipsEmptyValues()
        {
            var url = OidcRedirectUrlBuilder.BuildRedirectUri("https://cb.example.com", new Dictionary<string, string>
            {
                ["code"] = "abc",
                ["state"] = "s1",
                ["empty"] = ""
            });

            url.Should().StartWith("https://cb.example.com?");
            url.Should().Contain("code=abc");
            url.Should().Contain("state=s1");
            url.Should().NotContain("empty=");
            url.Should().NotEndWith("&");
        }

        [Fact]
        public void BuildRedirectUri_UsesAmpersand_WhenBaseAlreadyHasQuery()
        {
            var url = OidcRedirectUrlBuilder.BuildRedirectUri("https://cb.example.com?x=1", new Dictionary<string, string>
            {
                ["code"] = "abc"
            });

            url.Should().StartWith("https://cb.example.com?x=1&");
            url.Should().Contain("code=abc");
        }

        [Fact]
        public void BuildLoginUrl_IncludesAllParams_AndTenant()
        {
            var url = OidcRedirectUrlBuilder.BuildLoginUrl(
                "client 1", "code", "https://cb", "openid profile", "state-1", "nonce-1",
                "challenge", "S256", "tenant-1");

            url.Should().StartWith("/oidc/login?");
            url.Should().Contain("client_id=client%201");
            url.Should().Contain("response_type=code");
            url.Should().Contain("scope=openid%20profile");
            url.Should().Contain("code_challenge_method=S256");
            url.Should().Contain("tenant_id=tenant-1");
        }

        [Fact]
        public void BuildLoginUrl_OmitsTenant_WhenNull()
        {
            var url = OidcRedirectUrlBuilder.BuildLoginUrl(
                "c1", "code", "https://cb", "openid", "s", "n", "ch", "S256", null);

            url.Should().NotContain("tenant_id=");
        }

        [Fact]
        public void TryReadBasicClientAuthentication_ParsesValidHeader()
        {
            var ctx = new DefaultHttpContext();
            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes("client-1:secret-1"));
            ctx.Request.Headers["Authorization"] = $"Basic {creds}";

            OidcRedirectUrlBuilder.TryReadBasicClientAuthentication(ctx.Request, out var clientId, out var clientSecret);

            clientId.Should().Be("client-1");
            clientSecret.Should().Be("secret-1");
        }

        [Fact]
        public void TryReadBasicClientAuthentication_ReturnsEmpty_ForNonBasicScheme()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["Authorization"] = "Bearer token";

            OidcRedirectUrlBuilder.TryReadBasicClientAuthentication(ctx.Request, out var clientId, out var clientSecret);

            clientId.Should().BeEmpty();
            clientSecret.Should().BeEmpty();
        }

        [Fact]
        public void TryReadBasicClientAuthentication_ReturnsEmpty_ForMalformedBase64()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["Authorization"] = "Basic !!!notbase64!!!";

            OidcRedirectUrlBuilder.TryReadBasicClientAuthentication(ctx.Request, out var clientId, out var clientSecret);

            clientId.Should().BeEmpty();
            clientSecret.Should().BeEmpty();
        }

        [Fact]
        public void TryReadBasicClientAuthentication_ReturnsEmpty_WhenNoColonSeparator()
        {
            var ctx = new DefaultHttpContext();
            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes("nocolonhere"));
            ctx.Request.Headers["Authorization"] = $"Basic {creds}";

            OidcRedirectUrlBuilder.TryReadBasicClientAuthentication(ctx.Request, out var clientId, out var clientSecret);

            clientId.Should().BeEmpty();
            clientSecret.Should().BeEmpty();
        }

        [Fact]
        public void GetClientIpAddress_ReturnsRemoteIp()
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");

            OidcRedirectUrlBuilder.GetClientIpAddress(ctx.Request).Should().Be("203.0.113.5");
        }

        [Fact]
        public void GetClientIpAddress_ReturnsUnknown_WhenNoRemoteIp()
        {
            var ctx = new DefaultHttpContext();
            OidcRedirectUrlBuilder.GetClientIpAddress(ctx.Request).Should().Be("unknown");
        }

        [Fact]
        public void BuildVerificationUri_IncludesTenantAndUserCode()
        {
            var url = OidcRedirectUrlBuilder.BuildVerificationUri("https://idp.example.com/", "ABC-123", "tenant-1");
            url.Should().Be("https://idp.example.com/device/tenant-1?user_code=ABC-123");
        }
    }
}
