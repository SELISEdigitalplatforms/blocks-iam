using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Auth.Oidc
{
    public class OidcRedirectUrlBuilderTests
    {
        [Fact]
        public void BuildVerificationUri_NoQuery_WhenNoTenant()
        {
            var url = OidcRedirectUrlBuilder.BuildVerificationUri("https://idp.example.com", null, null);
            url.Should().Be("https://idp.example.com/device");
        }

        [Fact]
        public void BuildVerificationUri_TrailingSlashTrimmed()
        {
            var url = OidcRedirectUrlBuilder.BuildVerificationUri("https://idp.example.com/", null, null);
            url.Should().Be("https://idp.example.com/device");
        }

        [Fact]
        public void BuildVerificationUri_IncludesTenantId()
        {
            var url = OidcRedirectUrlBuilder.BuildVerificationUri("https://idp.example.com", null, "tenant-a");
            url.Should().Be("https://idp.example.com/device/tenant-a");
        }

        [Fact]
        public void BuildVerificationUriComplete_EncodesUserCode()
        {
            var url = OidcRedirectUrlBuilder.BuildVerificationUriComplete("https://idp.example.com", "ABCD-EFGH", "t1");
            url.Should().Be("https://idp.example.com/device/t1?user_code=ABCD-EFGH");
        }

        [Fact]
        public void ResolvePublicBaseUrl_PrefersConfiguredPublicBaseUrl_OverRequestScheme()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "http";
            ctx.Request.Host = new HostString("internal.svc.cluster.local");

            var url = OidcRedirectUrlBuilder.ResolvePublicBaseUrl(ctx.Request, "https://iam.seliseblocks.com");

            url.Should().Be("https://iam.seliseblocks.com");
        }

        [Fact]
        public void ResolvePublicBaseUrl_StripsTrailingSlash_OnConfiguredPublicBaseUrl()
        {
            var ctx = new DefaultHttpContext();

            var url = OidcRedirectUrlBuilder.ResolvePublicBaseUrl(ctx.Request, "https://iam.seliseblocks.com/");

            url.Should().Be("https://iam.seliseblocks.com");
        }

        [Fact]
        public void ResolvePublicBaseUrl_FallsBackToRequest_WhenPublicBaseUrlBlank()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Scheme = "https";
            ctx.Request.Host = new HostString("idp.example.com");

            OidcRedirectUrlBuilder.ResolvePublicBaseUrl(ctx.Request, null)
                .Should().Be("https://idp.example.com");
            OidcRedirectUrlBuilder.ResolvePublicBaseUrl(ctx.Request, "")
                .Should().Be("https://idp.example.com");
            OidcRedirectUrlBuilder.ResolvePublicBaseUrl(ctx.Request, "   ")
                .Should().Be("https://idp.example.com");
        }
    }
}