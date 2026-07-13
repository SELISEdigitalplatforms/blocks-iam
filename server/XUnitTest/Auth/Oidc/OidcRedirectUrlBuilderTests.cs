using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Contracts;
using FluentAssertions;

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
            url.Should().Be("https://idp.example.com/device?tenant_id=tenant-a");
        }

        [Fact]
        public void BuildVerificationUriComplete_EncodesUserCode()
        {
            var url = OidcRedirectUrlBuilder.BuildVerificationUriComplete("https://idp.example.com", "ABCD-EFGH", "t1");
            url.Should().Be("https://idp.example.com/device?tenant_id=t1&user_code=ABCD-EFGH");
        }
    }
}