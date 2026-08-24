using FluentAssertions;
using Iam.DomainService.Utilities;
using Xunit;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// The scope decision is a pure function, so it is tested here without a database or an ambient
    /// context. The cases that matter most are the ones the old ResolveOrganizationId collapsed:
    /// a blank token organization must deny rather than become "default" (fail-open), and an
    /// explicitly requested "default" must scope rather than mean "every organization".
    /// </summary>
    public sealed class UserListOrganizationScopeTests
    {
        [Theory] // C1 -- rule 4, and it must win over rule 1
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_WithNoTokenOrganization_Denies(string? tokenOrganizationId)
        {
            var scope = UserListOrganizationScope.Resolve(tokenOrganizationId, null);

            scope.Kind.Should().Be(UserListScopeKind.Denied);
            scope.OrganizationIds.Should().BeEmpty();
        }

        [Fact] // C1 -- a requested list must not rescue a token that names no organization
        public void Resolve_WithNoTokenOrganization_DeniesEvenWhenOrganizationsAreRequested()
        {
            var scope = UserListOrganizationScope.Resolve("", ["org-a", "org-b"]);

            scope.Kind.Should().Be(UserListScopeKind.Denied);
        }

        [Theory] // H1 -- rule 1: nothing requested means the whole tenant
        [InlineData(null)]
        public void Resolve_DefaultTokenWithNullList_AllowsEveryOrganization(string[]? requested)
        {
            var scope = UserListOrganizationScope.Resolve("default", requested);

            scope.Kind.Should().Be(UserListScopeKind.AllOrganizations);
            scope.OrganizationIds.Should().BeEmpty();
        }

        [Fact] // H1
        public void Resolve_DefaultTokenWithEmptyList_AllowsEveryOrganization()
        {
            UserListOrganizationScope.Resolve("default", [])
                .Kind.Should().Be(UserListScopeKind.AllOrganizations);
        }

        [Fact] // H2 -- rule 2 with a single id
        public void Resolve_DefaultTokenWithOneOrganization_ScopesToIt()
        {
            var scope = UserListOrganizationScope.Resolve("default", ["org-a"]);

            scope.Kind.Should().Be(UserListScopeKind.Organizations);
            scope.OrganizationIds.Should().Equal("org-a");
        }

        [Fact] // H3 -- rule 2 with several ids, order preserved
        public void Resolve_DefaultTokenWithSeveralOrganizations_ScopesToAllOfThem()
        {
            var scope = UserListOrganizationScope.Resolve("default", ["org-a", "org-b", "org-c"]);

            scope.Kind.Should().Be(UserListScopeKind.Organizations);
            scope.OrganizationIds.Should().Equal("org-a", "org-b", "org-c");
        }

        [Fact] // C3 -- an explicit "default" scopes to that organization, it does not widen
        public void Resolve_DefaultTokenRequestingDefault_ScopesToDefaultOnly()
        {
            var scope = UserListOrganizationScope.Resolve("default", ["default"]);

            scope.Kind.Should().Be(UserListScopeKind.Organizations);
            scope.OrganizationIds.Should().Equal("default");
        }

        [Fact] // H4 -- rule 3 pins a non-default caller to its own organization
        public void Resolve_NonDefaultToken_ScopesToTheTokenOrganization()
        {
            var scope = UserListOrganizationScope.Resolve("org-a", null);

            scope.Kind.Should().Be(UserListScopeKind.Organizations);
            scope.OrganizationIds.Should().Equal("org-a");
        }

        [Fact] // C2 -- the security case: a payload can never widen what the token authorises
        public void Resolve_NonDefaultToken_DiscardsEveryRequestedOrganization()
        {
            var scope = UserListOrganizationScope.Resolve("org-a", ["org-b", "org-c", "default"]);

            scope.Kind.Should().Be(UserListScopeKind.Organizations);
            scope.OrganizationIds.Should().Equal("org-a");
            scope.OrganizationIds.Should().NotContain("org-b").And.NotContain("org-c");
        }

        [Fact] // C8 -- blanks dropped, duplicates collapsed, caller order kept
        public void Resolve_SanitisesBlanksAndDuplicates()
        {
            var scope = UserListOrganizationScope.Resolve("default", ["org-b", "", "org-a", "  ", "org-b"]);

            scope.OrganizationIds.Should().Equal("org-b", "org-a");
        }

        [Fact] // C8 -- a list that reduces to nothing behaves as an absent list
        public void Resolve_BlankOnlyList_FallsBackToEveryOrganization()
        {
            UserListOrganizationScope.Resolve("default", ["", "   "])
                .Kind.Should().Be(UserListScopeKind.AllOrganizations);
        }

        [Fact] // C8 -- de-duplication is ordinal, so case variants are distinct ids
        public void Resolve_DeDuplicationIsOrdinal()
        {
            UserListOrganizationScope.Resolve("default", ["org-a", "ORG-A"])
                .OrganizationIds.Should().Equal("org-a", "ORG-A");
        }

        [Fact] // C3 -- the "default" comparison is exact: no trimming, no case folding
        public void Resolve_TokenOrganizationComparisonIsExact()
        {
            UserListOrganizationScope.Resolve("Default", null)
                .Should().BeEquivalentTo(new UserListScope(UserListScopeKind.Organizations, ["Default"]));

            UserListOrganizationScope.Resolve(" default ", null)
                .Kind.Should().Be(UserListScopeKind.Organizations);
        }
    }
}
