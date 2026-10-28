using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Blocks.Genesis.Entities.Iam;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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

        private static IConfiguration Config(string? iamBaseUrl)
        {
            var values = new Dictionary<string, string?>();

            if (iamBaseUrl != null)
            {
                values["FrontendRuntime:BLOCKS_IAM_BASE_URL"] = iamBaseUrl;
            }

            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }

        /// <summary>
        /// BLOCKS_IAM_BASE_URL is read from the process environment before IConfiguration,
        /// so pin it for the duration of the test rather than inheriting the dev's shell.
        /// </summary>
        private static void WithEnvIamBaseUrl(string? value, Action body)
        {
            var previous = Environment.GetEnvironmentVariable("BLOCKS_IAM_BASE_URL");
            Environment.SetEnvironmentVariable("BLOCKS_IAM_BASE_URL", value);
            try
            {
                body();
            }
            finally
            {
                Environment.SetEnvironmentVariable("BLOCKS_IAM_BASE_URL", previous);
            }
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

        private const string TenantId = "tenant-1";

        /// <summary>A request from <paramref name="origin"/> to a tenant whose app is app.example.com.</summary>
        private static HttpContext TenantRequest(string? origin, string? referer = null, bool development = false)
        {
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(new Tenant
            {
                DbConnectionString = "mongodb://x",
                JwtTokenParameters = new JwtTokenParameters { IssueDate = DateTime.UtcNow, PrivateCertificatePassword = "p" },
                Applications = new List<Applications> { new() { Domain = "https://app.example.com" } }
            });
            var environment = new Mock<IHostEnvironment>();
            environment.Setup(e => e.EnvironmentName).Returns(development ? Environments.Development : Environments.Production);
            var services = new ServiceCollection().AddSingleton(tenants.Object).AddSingleton(environment.Object).BuildServiceProvider();

            var context = ContextWith(origin: origin, referer: referer);
            context.RequestServices = services;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: TenantId, roles: null, userId: string.Empty, isAuthenticated: false, requestUri: "https://iam.test",
                organizationId: string.Empty, expireOn: DateTime.UtcNow.AddHours(1), email: string.Empty, permissions: null,
                userName: string.Empty, phoneNumber: string.Empty, displayName: string.Empty, oauthToken: string.Empty,
                originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: string.Empty));
            return context;
        }

        [Fact]
        public void GetTrustedOriginBaseUrl_TenantDomainOverHttps_IsUsed()
        {
            IamHelper.GetTrustedOriginBaseUrl(Accessor(TenantRequest("https://app.example.com")))
                .Should().Be("https://app.example.com");
        }

        [Fact]
        public void GetTrustedOriginBaseUrl_ForeignOrigin_IsRefused_EvenWithALocalhostReferer()
        {
            // A reset link must never point at a domain the tenant does not own.
            IamHelper.GetTrustedOriginBaseUrl(Accessor(TenantRequest("https://evil.example.net", referer: "http://localhost:3000")))
                .Should().BeEmpty();
        }

        [Fact]
        public void GetTrustedOriginBaseUrl_RefererAlone_IsNeverUsed()
        {
            IamHelper.GetTrustedOriginBaseUrl(Accessor(TenantRequest(null, referer: "https://app.example.com/page")))
                .Should().BeEmpty();
        }

        [Fact]
        public void GetTrustedOriginBaseUrl_TenantDomainOverHttp_IsRefused()
        {
            IamHelper.GetTrustedOriginBaseUrl(Accessor(TenantRequest("http://app.example.com")))
                .Should().BeEmpty();
        }

        [Fact]
        public void GetTrustedOriginBaseUrl_Localhost_OnlyInDevelopment()
        {
            IamHelper.GetTrustedOriginBaseUrl(Accessor(TenantRequest("http://localhost:3000"))).Should().BeEmpty();
            IamHelper.GetTrustedOriginBaseUrl(Accessor(TenantRequest("http://localhost:3000", development: true)))
                .Should().Be("http://localhost:3000");
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
        public void TryBuildUserActionUrl_OidcEnabled_PrefersConfiguredIamBaseUrl_OverRequestHost()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = false,
                IsOidcEnabled = true,
                AccountActionBaseUrl = "https://stale.test"
            };

            // A forged Host header must not steer the link.
            var accessor = Accessor(ContextWith(host: "attacker.test"));

            WithEnvIamBaseUrl(null, () =>
            {
                IamHelper.TryBuildUserActionUrl(
                    config, "/recover", out var url, accessor,
                    appConfiguration: Config("https://iam.test")).Should().BeTrue();

                url.Should().Be("https://iam.test/recover");
            });
        }

        [Fact]
        public void TryBuildUserActionUrl_OidcEnabled_ReadsIamBaseUrlFromEnvironment()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = false,
                IsOidcEnabled = true,
                AccountActionBaseUrl = "https://stale.test"
            };

            WithEnvIamBaseUrl("https://iam-from-env.test", () =>
            {
                IamHelper.TryBuildUserActionUrl(
                    config, "/recover", out var url, Accessor(ContextWith(host: "attacker.test")),
                    appConfiguration: Config(null)).Should().BeTrue();

                url.Should().Be("https://iam-from-env.test/recover");
            });
        }

        [Fact]
        public void TryBuildUserActionUrl_OidcEnabled_FallsBackToStoredBaseUrl_WhenNotConfigured()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = false,
                IsOidcEnabled = true,
                AccountActionBaseUrl = "https://stored.test"
            };

            var accessor = Accessor(ContextWith(host: "attacker.test", origin: "https://origin.test"));

            WithEnvIamBaseUrl(null, () =>
            {
                IamHelper.TryBuildUserActionUrl(
                    config, "/recover", out var url, accessor,
                    appConfiguration: Config(null)).Should().BeTrue();

                url.Should().Be("https://stored.test/recover");
            });
        }

        [Fact]
        public void TryBuildUserActionUrl_OidcEnabled_NeverUsesOriginOrReferer()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = false,
                IsOidcEnabled = true,
                AccountActionBaseUrl = "https://stored.test"
            };

            var accessor = Accessor(ContextWith(origin: "https://origin.test"));

            WithEnvIamBaseUrl(null, () =>
            {
                IamHelper.TryBuildUserActionUrl(
                    config, "/recover", out var url, accessor,
                    appConfiguration: Config(null)).Should().BeTrue();

                url.Should().NotContain("origin.test");
                url.Should().Be("https://stored.test/recover");
            });
        }

        [Fact]
        public void TryBuildUserActionUrl_OidcEnabled_UsesRequestHost_OnlyAsLastResort()
        {
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = false,
                IsOidcEnabled = true,
                AccountActionBaseUrl = string.Empty
            };

            WithEnvIamBaseUrl(null, () =>
            {
                IamHelper.TryBuildUserActionUrl(
                    config, "/recover", out var url, Accessor(ContextWith(host: "oidc.test")),
                    appConfiguration: Config(null)).Should().BeTrue();

                url.Should().Be("https://oidc.test/recover");
            });
        }

        [Fact]
        public void TryBuildUserActionUrl_OidcEnabled_NoContextAtAll_StillUsesConfiguredBaseUrl()
        {
            // The Worker builds invite mail off a queue message, with no HttpContext at all.
            var config = new IamConfiguration
            {
                UseAccountActionBaseUrlAsDefault = false,
                IsOidcEnabled = true,
                AccountActionBaseUrl = "https://stored.test"
            };

            WithEnvIamBaseUrl(null, () =>
            {
                IamHelper.TryBuildUserActionUrl(
                    config, "/activate", out var url, httpContextAccessor: null,
                    appConfiguration: Config("https://iam.test")).Should().BeTrue();

                url.Should().Be("https://iam.test/activate");
            });
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
