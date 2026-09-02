using FluentAssertions;
using Iam.DomainService.Utilities;
using Xunit;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// The organization-endpoint scope decision is a pure function of the token's organization
    /// claim, so it is tested here with no database and no ambient context.
    /// <para>
    /// The cases that matter most are the fail-open ones: a blank claim and "no-org" must deny
    /// rather than collapse to "default", because "default" is the tenant-wide scope and an unknown
    /// answer must never become the most privileged one.
    /// </para>
    /// </summary>
    public sealed class OrganizationAccessScopeResolverTests
    {
        [Theory] // A blank claim denies, and must be tested before the "default" comparison.
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_WithNoTokenOrganization_Denies(string? tokenOrganizationId)
        {
            var scope = OrganizationAccessScopeResolver.Resolve(tokenOrganizationId);

            scope.Kind.Should().Be(OrganizationAccessScopeKind.Denied);
            scope.OrganizationId.Should().BeEmpty();
        }

        [Fact] // "no-org" is an explicit "belongs to nothing", not an organization id matching nothing.
        public void Resolve_WithNoOrganizationSentinel_Denies()
        {
            var scope = OrganizationAccessScopeResolver.Resolve("no-org");

            scope.Kind.Should().Be(OrganizationAccessScopeKind.Denied);
        }

        [Fact]
        public void Resolve_WithDefaultOrganization_AllowsEveryOrganization()
        {
            var scope = OrganizationAccessScopeResolver.Resolve("default");

            scope.Kind.Should().Be(OrganizationAccessScopeKind.AllOrganizations);
        }

        [Fact]
        public void Resolve_WithRealOrganization_PinsToThatOrganization()
        {
            var scope = OrganizationAccessScopeResolver.Resolve("org-a");

            scope.Kind.Should().Be(OrganizationAccessScopeKind.Organization);
            scope.OrganizationId.Should().Be("org-a");
        }

        [Fact] // The claim arrives from a JWT; surrounding whitespace must not create a distinct scope.
        public void Resolve_TrimsTheClaim()
        {
            OrganizationAccessScopeResolver.Resolve("  org-a  ").OrganizationId.Should().Be("org-a");
        }

        [Fact] // Casing is not folded: ids are matched verbatim against stored ids downstream.
        public void Resolve_DoesNotFoldCase()
        {
            var scope = OrganizationAccessScopeResolver.Resolve("Default");

            scope.Kind.Should().Be(OrganizationAccessScopeKind.Organization);
            scope.OrganizationId.Should().Be("Default");
        }

        // ---------- Allows ----------

        [Fact]
        public void Allows_TenantWide_AcceptsAnyOrganization()
        {
            var scope = OrganizationAccessScopeResolver.Resolve("default");

            scope.Allows("org-a").Should().BeTrue();
            scope.Allows("anything-at-all").Should().BeTrue();
        }

        [Fact]
        public void Allows_PinnedScope_AcceptsOnlyItsOwnOrganization()
        {
            var scope = OrganizationAccessScopeResolver.Resolve("org-a");

            scope.Allows("org-a").Should().BeTrue();
            scope.Allows("org-b").Should().BeFalse();
        }

        [Theory] // A denied scope admits nothing, including the sentinels themselves.
        [InlineData("org-a")]
        [InlineData("default")]
        [InlineData(null)]
        public void Allows_DeniedScope_AcceptsNothing(string? organizationId)
        {
            var scope = OrganizationAccessScopeResolver.Resolve(null);

            scope.Allows(organizationId).Should().BeFalse();
        }

        [Fact]
        public void Allows_PinnedScope_RejectsNull()
        {
            OrganizationAccessScopeResolver.Resolve("org-a").Allows(null).Should().BeFalse();
        }
    }
}
