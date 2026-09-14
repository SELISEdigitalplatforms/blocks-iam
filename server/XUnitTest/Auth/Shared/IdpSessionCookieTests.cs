using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Utilities;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Auth.Shared
{
    /// <summary>
    /// The Domain rules of the single IdP session cookie writer, and the sweep its clear performs.
    /// </summary>
    public class IdpSessionCookieTests
    {
        private const string AppOrigin = "https://app.example.com";
        private const string TenantId = "tenant-1";

        private static Tenant TenantWith(bool isRoot, string? cookieDomain) => new()
        {
            TenantId = TenantId,
            IsRootTenant = isRoot,
            DbConnectionString = string.Empty,
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
            Applications = new List<Applications> { new() { Domain = AppOrigin, CookieDomain = cookieDomain } }
        };

        private static DefaultHttpContext RequestFromApp()
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers["Origin"] = AppOrigin;
            return ctx;
        }

        [Fact]
        public void ResolveCookieDomain_ExplicitCookieDomain_Wins_EvenForRootTenant()
        {
            IdpSessionCookie.ResolveCookieDomain(TenantWith(isRoot: true, cookieDomain: ".example.com"), RequestFromApp().Request)
                .Should().Be(".example.com");
        }

        [Fact]
        public void ResolveCookieDomain_RootTenantWithoutCookieDomain_CollapsesToRootDomain()
        {
            IdpSessionCookie.ResolveCookieDomain(TenantWith(isRoot: true, cookieDomain: null), RequestFromApp().Request)
                .Should().Be("example.com");
        }

        [Fact]
        public void ResolveCookieDomain_NonRootTenantWithoutCookieDomain_UsesApplicationHost()
        {
            IdpSessionCookie.ResolveCookieDomain(TenantWith(isRoot: false, cookieDomain: null), RequestFromApp().Request)
                .Should().Be("app.example.com");
        }

        [Fact]
        public void ResolveCookieDomain_UnresolvedRequest_IsHostOnly()
        {
            IdpSessionCookie.ResolveCookieDomain(TenantWith(isRoot: true, cookieDomain: null), new DefaultHttpContext().Request)
                .Should().BeNull();
            IdpSessionCookie.ResolveCookieDomain(null, RequestFromApp().Request).Should().BeNull();
        }

        [Fact]
        public void Append_And_Clear_UseTheSameDomain()
        {
            var tenant = TenantWith(isRoot: true, cookieDomain: null);
            var ctx = RequestFromApp();
            var cookieKey = IdpConstants.BuildIdpSessionCookieKey(TenantId);

            IdpSessionCookie.Append(ctx.Request, ctx.Response, tenant, TenantId, "sess-1", DateTime.UtcNow.AddDays(1));
            var written = ctx.Response.Headers.SetCookie.ToString();
            written.Should().StartWith(cookieKey + "=sess-1");
            written.Should().Contain("domain=example.com");

            var clearCtx = RequestFromApp();
            IdpSessionCookie.Clear(clearCtx.Request, clearCtx.Response, tenant, TenantId);
            clearCtx.Response.Headers.SetCookie.ToArray()
                .Should().Contain(c => c!.Contains("domain=example.com", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ResolveClearScopes_CoversEveryHistoricalScope_Once()
        {
            var scopes = IdpSessionCookie.ResolveClearScopes(TenantWith(isRoot: true, cookieDomain: ".example.com"), RequestFromApp().Request);

            scopes.Should().BeEquivalentTo(new string?[] { null, ".example.com", "app.example.com", "example.com" });
        }

        [Fact]
        public void ResolveClearScopes_UnresolvedRequest_IsHostOnlyOnly()
        {
            IdpSessionCookie.ResolveClearScopes(null, new DefaultHttpContext().Request)
                .Should().BeEquivalentTo(new string?[] { null });
        }
    }
}
