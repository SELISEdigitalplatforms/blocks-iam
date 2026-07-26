using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Moq;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// Branch coverage for <see cref="IamHelper"/>, the static URL resolution helper used when
    /// building account action links. Exercises the OIDC, origin/referer and default fallbacks.
    /// </summary>
    public class IamHelperTests
    {
        private static IHttpContextAccessor Accessor(HttpContext? context)
        {
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(context);
            return accessor.Object;
        }

        private static HttpContext ContextWith(string? host = null, string? origin = null, string? referer = null)
        {
            var context = new DefaultHttpContext();
            if (host != null)
            {
                context.Request.Host = new HostString(host);
            }
            if (origin != null)
            {
                context.Request.Headers["Origin"] = origin;
            }
            if (referer != null)
            {
                context.Request.Headers["Referer"] = referer;
            }
            return context;
        }

        [Fact]
        public void GetOidcRequestBaseUrl_NullAccessor_ReturnsEmpty()
        {
            IamHelper.GetOidcRequestBaseUrl(null).Should().BeEmpty();
        }

        [Fact]
        public void GetOidcRequestBaseUrl_NoHost_ReturnsEmpty()
        {
            IamHelper.GetOidcRequestBaseUrl(Accessor(new DefaultHttpContext())).Should().BeEmpty();
        }

        [Fact]
        public void GetOidcRequestBaseUrl_WithHost_ReturnsHttpsHost()
        {
            IamHelper.GetOidcRequestBaseUrl(Accessor(ContextWith(host: "example.com")))
                .Should().Be("https://example.com");
        }

        [Fact]
        public void GetOriginOrRefererBaseUrl_NullRequest_ReturnsEmpty()
        {
            IamHelper.GetOriginOrRefererBaseUrl(Accessor(null)).Should().BeEmpty();
        }

        [Fact]
        public void GetOriginOrRefererBaseUrl_UsesOrigin()
        {
            IamHelper.GetOriginOrRefererBaseUrl(Accessor(ContextWith(origin: "https://origin.test/path")))
                .Should().Be("https://origin.test");
        }

        [Fact]
        public void GetOriginOrRefererBaseUrl_FallsBackToReferer()
        {
            IamHelper.GetOriginOrRefererBaseUrl(Accessor(ContextWith(referer: "http://referer.test/x")))
                .Should().Be("http://referer.test");
        }

        [Fact]
        public void GetOriginOrRefererBaseUrl_NonHttpScheme_ReturnsEmpty()
        {
            IamHelper.GetOriginOrRefererBaseUrl(Accessor(ContextWith(origin: "ftp://origin.test")))
                .Should().BeEmpty();
        }

        [Fact]
        public void GetOriginOrRefererBaseUrl_InvalidUrl_ReturnsEmpty()
        {
            IamHelper.GetOriginOrRefererBaseUrl(Accessor(ContextWith(origin: "not-a-url")))
                .Should().BeEmpty();
        }

        [Fact]
        public void TryBuildUserActionUrl_NullConfig_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                IamHelper.TryBuildUserActionUrl(null!, "/path", out _));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void TryBuildUserActionUrl_BlankPath_ReturnsFalse(string path)
        {
            var config = new IamConfiguration();
            IamHelper.TryBuildUserActionUrl(config, path, out var url).Should().BeFalse();
            url.Should().BeEmpty();
        }

        [Fact]
        public void TryBuildUserActionUrl_UsesAccountActionBaseUrl_WhenDefaultFlagSet()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = "https://acct.test/"
            };

            IamHelper.TryBuildUserActionUrl(config, "activate", out var url).Should().BeTrue();
            url.Should().Be("https://acct.test/activate");
        }

        [Fact]
        public void TryBuildUserActionUrl_LeadingSlashPath_NotDoubled()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = true,
                AccountActionBaseUrl = "https://acct.test"
            };

            IamHelper.TryBuildUserActionUrl(config, "/verify", out var url).Should().BeTrue();
            url.Should().Be("https://acct.test/verify");
        }

        [Fact]
        public void TryBuildUserActionUrl_OidcEnabled_UsesRequestBaseUrl()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = false,
                IsOidcEnabled = true,
                AccountActionBaseUrl = "https://fallback.test"
            };

            var accessor = Accessor(ContextWith(host: "oidc.test"));

            IamHelper.TryBuildUserActionUrl(config, "/recover", out var url, accessor).Should().BeTrue();
            url.Should().Be("https://oidc.test/recover");
        }

        [Fact]
        public void TryBuildUserActionUrl_OidcEnabledButNoHost_FallsBackToOriginReferer()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = false,
                IsOidcEnabled = true,
                AccountActionBaseUrl = "https://fallback.test"
            };

            var accessor = Accessor(ContextWith(origin: "https://origin.test"));

            IamHelper.TryBuildUserActionUrl(config, "/recover", out var url, accessor).Should().BeTrue();
            url.Should().Be("https://origin.test/recover");
        }

        [Fact]
        public void TryBuildUserActionUrl_FallsBackToAccountActionBaseUrl_WhenNothingElseResolves()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = false,
                IsOidcEnabled = false,
                AccountActionBaseUrl = "https://fallback.test/"
            };

            IamHelper.TryBuildUserActionUrl(config, "/recover", out var url, Accessor(null)).Should().BeTrue();
            url.Should().Be("https://fallback.test/recover");
        }

        [Fact]
        public void TryBuildUserActionUrl_NoBaseUrlAvailable_ReturnsFalse()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = false,
                IsOidcEnabled = false,
                AccountActionBaseUrl = string.Empty
            };

            IamHelper.TryBuildUserActionUrl(config, "/recover", out var url, Accessor(null)).Should().BeFalse();
            url.Should().BeEmpty();
        }
    }
}
