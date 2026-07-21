using System.Net;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Auth.Shared
{
    [Collection("DomainResolverStatic")]
    public class DomainResolverExtraTests : IDisposable
    {
        public DomainResolverExtraTests()
        {
            // Start every test from a clean accessor with no HttpContext.
            DomainResolver.Configure(new HttpContextAccessor());
        }

        public void Dispose()
        {
            DomainResolver.Configure(new HttpContextAccessor());
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static IHttpContextAccessor AccessorWith(string? origin = null, string? referer = null, IPAddress? remoteIp = null)
        {
            var ctx = new DefaultHttpContext();
            if (origin != null) ctx.Request.Headers["Origin"] = origin;
            if (referer != null) ctx.Request.Headers["Referer"] = referer;
            if (remoteIp != null) ctx.Connection.RemoteIpAddress = remoteIp;
            return new HttpContextAccessor { HttpContext = ctx };
        }

        [Fact]
        public void IsLocalhost_TrueForLoopbackOrigin()
        {
            DomainResolver.Configure(AccessorWith(origin: "http://localhost:3000"));
            DomainResolver.IsLocalhost().Should().BeTrue();
        }

        [Fact]
        public void IsLocalhost_TrueForNonDefaultPort()
        {
            DomainResolver.Configure(AccessorWith(origin: "https://staging.example.com:8080"));
            DomainResolver.IsLocalhost().Should().BeTrue();
        }

        [Fact]
        public void IsLocalhost_TrueForLoopbackRemoteIp()
        {
            DomainResolver.Configure(AccessorWith(remoteIp: IPAddress.Loopback));
            DomainResolver.IsLocalhost().Should().BeTrue();
        }

        [Fact]
        public void IsLocalhost_FalseForProductionOriginOnDefaultPort()
        {
            DomainResolver.Configure(AccessorWith(origin: "https://app.example.com", remoteIp: IPAddress.Parse("203.0.113.7")));
            DomainResolver.IsLocalhost().Should().BeFalse();
        }

        [Fact]
        public void CreateCookieOptions_UsesLoopbackWhenLocal()
        {
            DomainResolver.Configure(AccessorWith(origin: "http://localhost:5173"));
            var options = DomainResolver.CreateCookieOptions("app.example.com", DateTime.UtcNow.AddHours(1));
            options.SameSite.Should().Be(SameSiteMode.None);
            options.HttpOnly.Should().BeTrue();
        }

        [Fact]
        public void CreateCookieOptions_UsesProductionWhenNotLocal()
        {
            DomainResolver.Configure(AccessorWith(origin: "https://app.example.com", remoteIp: IPAddress.Parse("198.51.100.9")));
            var options = DomainResolver.CreateCookieOptions("app.example.com", DateTime.UtcNow.AddHours(1));
            options.SameSite.Should().Be(SameSiteMode.Strict);
            options.HttpOnly.Should().BeTrue();
            options.Secure.Should().BeTrue();
        }

        [Fact]
        public void ResolveDomain_ReturnsResolved_WhenOriginMatchesApplication()
        {
            var tenant = new Tenant
            {
                TenantId = "t1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
                Applications = new List<Applications>
                {
                    new() { Domain = "https://app.example.com", CookieDomain = ".example.com" }
                }
            };
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["Origin"] = "https://app.example.com";

            var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, ctx.Request);

            isResolved.Should().BeTrue();
            domain.Should().NotBeNullOrEmpty();
            cookieDomain.Should().Be(".example.com");
        }

        [Fact]
        public void ResolveDomain_ReturnsUnresolved_WhenOriginDoesNotMatch()
        {
            var tenant = new Tenant
            {
                TenantId = "t1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
                Applications = new List<Applications> { new() { Domain = "https://app.example.com" } }
            };
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["Origin"] = "https://evil.other.com";

            var (_, _, isResolved) = DomainResolver.ResolveDomain(tenant, ctx.Request);

            isResolved.Should().BeFalse();
        }

        [Fact]
        public void ResolveDomain_ReturnsUnresolved_WhenOriginIsLocalhost()
        {
            var tenant = new Tenant
            {
                TenantId = "t1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
                Applications = new List<Applications> { new() { Domain = "https://app.example.com" } }
            };
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["Origin"] = "http://localhost:3000";

            var (_, _, isResolved) = DomainResolver.ResolveDomain(tenant, ctx.Request);

            isResolved.Should().BeFalse();
        }

        [Fact]
        public void GetAudience_ReturnsConfiguredAudience_WhenPresent()
        {
            var tenant = new Tenant
            {
                TenantId = "t1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = string.Empty,
                    IssueDate = DateTime.UtcNow,
                    Audiences = new List<string> { "  api://custom-aud  " }
                },
                Applications = new List<Applications>()
            };

            DomainResolver.GetAudience(tenant).Should().Be("api://custom-aud");
        }

        [Fact]
        public void GetIssuer_ReturnsConfiguredIssuer_WhenPresent()
        {
            var tenant = new Tenant
            {
                TenantId = "t1",
                DbConnectionString = string.Empty,
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = string.Empty,
                    IssueDate = DateTime.UtcNow,
                    Issuer = "  https://issuer.example.com  "
                },
                Applications = new List<Applications>()
            };

            DomainResolver.GetIssuer(tenant).Should().Be("https://issuer.example.com");
        }

        [Fact]
        public void ResetToOriginalBlocksContextForImpersonation_RestoresOriginalTenant_WhenImpersonating()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "impersonated-tenant", roles: null, userId: "user-1", impersonated: true,
                isAuthenticated: true, requestUri: "https://test", organizationId: "org-1",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "original-tenant", impersonationSessionId: "sess-1", applicationDomain: "test"));

            DomainResolver.ResetToOriginalBlocksContextForImpersonation();

            var ctx = BlocksContext.GetContext();
            ctx.Should().NotBeNull();
            ctx!.TenantId.Should().Be("original-tenant");
            ctx.Impersonated.Should().BeFalse();
        }

        [Fact]
        public void ResetToOriginalBlocksContextForImpersonation_NoOp_WhenNotImpersonating()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: "tenant-1", roles: null, userId: "user-1", impersonated: false,
                isAuthenticated: true, requestUri: "https://test", organizationId: "org-1",
                permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
                userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
                originalTenantId: "tenant-1", impersonationSessionId: null, applicationDomain: "test"));

            DomainResolver.ResetToOriginalBlocksContextForImpersonation();

            BlocksContext.GetContext()!.TenantId.Should().Be("tenant-1");
        }
    }
}
