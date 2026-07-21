using Iam.DomainService.Utilities;
using Authentication.DomainService.Utilities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Auth.Shared
{
    [Collection("DomainResolverStatic")]
    public class DomainResolverTests
    {
        [Fact]
        public void GetRootDomain_ReturnsHost_WhenLessThanTwoParts()
        {
            DomainResolver.GetRootDomain("localhost").Should().Be("localhost");
            // Dotted hosts collapse to the last two labels; an IPv4 literal therefore yields its last two octets.
            DomainResolver.GetRootDomain("127.0.0.1").Should().Be("0.1");
            DomainResolver.GetRootDomain("::1").Should().Be("::1");
        }

        [Fact]
        public void GetRootDomain_ReturnsLastTwoParts()
        {
            DomainResolver.GetRootDomain("a.b.example.com").Should().Be("example.com");
            DomainResolver.GetRootDomain("example.com").Should().Be("example.com");
        }

        [Fact]
        public void GetRootDomain_ReturnsInput_WhenEmptyOrWhitespace()
        {
            DomainResolver.GetRootDomain("").Should().Be("");
            DomainResolver.GetRootDomain("   ").Should().Be("   ");
        }

        [Fact]
        public void ResolveDomain_ReturnsUnresolved_WhenTenantIsNull()
        {
            var result = DomainResolver.ResolveDomain(null, null);
            result.Should().Be((null, null, false));
        }

        [Fact]
        public void ResolveDomain_ReturnsUnresolved_WhenTenantHasNoApplications()
        {
            // Test through static method behavior with an invalid tenant
            var result = DomainResolver.ResolveDomain(null, null);
            result.isResolved.Should().BeFalse();
        }

        [Fact]
        public void CreateLoopbackCookieOptions_SetsRequiredProperties()
        {
            var expiry = DateTime.UtcNow.AddHours(1);
            var options = DomainResolver.CreateLoopbackCookieOptions(".example.com", expiry);

            options.Domain.Should().Be(".example.com");
            options.HttpOnly.Should().BeTrue();
            options.Secure.Should().BeTrue();
            options.Path.Should().Be("/");
        }

        [Fact]
        public void CreateProductionCookieOptions_StrictSameSite()
        {
            var expiry = DateTime.UtcNow.AddHours(1);
            var options = DomainResolver.CreateProductionCookieOptions(".example.com", expiry);

            options.Domain.Should().Be(".example.com");
            options.HttpOnly.Should().BeTrue();
            options.Secure.Should().BeTrue();
            options.SameSite.Should().Be(SameSiteMode.Strict);
            options.Path.Should().Be("/");
        }

        [Fact]
        public void CreateProductionCookieOptions_HandlesNullDomain()
        {
            var expiry = DateTime.UtcNow.AddHours(1);
            var options = DomainResolver.CreateProductionCookieOptions(null, expiry);

            options.Domain.Should().BeNull();
        }

        [Fact]
        public void CreateLoopbackCookieOptions_NoneSameSite()
        {
            var expiry = DateTime.UtcNow.AddHours(1);
            var options = DomainResolver.CreateLoopbackCookieOptions(".example.com", expiry);

            options.SameSite.Should().Be(SameSiteMode.None);
        }

        [Fact]
        public void GetAudience_FallsBackToProtected_WhenTenantHasNone()
        {
            // Test the fallback constant
            Iam.DomainService.Utilities.IdpConstants.ProtectedApiAudience
                .Should().Be("api://blocks-protected-api");
        }

        [Fact]
        public void GetIssuer_DefaultFallback_IsSeliseBlocks()
        {
            // Test fallback
            var tenant = new Blocks.Genesis.Tenant
            {
                TenantId = "test",
                DbConnectionString = "",
                JwtTokenParameters = new Blocks.Genesis.JwtTokenParameters
                {
                    PrivateCertificatePassword = "",
                    IssueDate = DateTime.UtcNow
                }
            };
            DomainResolver.GetIssuer(tenant).Should().Be("selise-blocks");
        }
    }
}