using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Utilities;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// The R0-R6 truth table of <see cref="OrganizationScopeResolver"/>, plus every example in
    /// SPEC10 §4. The membership test is the real one from
    /// <c>OrganizationAccessResolver.HasOrganizationAccess</c>, restated here as a local delegate
    /// so this suite stays inside Iam.DomainService and does not depend on the auth assembly.
    /// </summary>
    public class OrganizationScopeResolverTests
    {
        private static bool HasAccess(User user, string? organizationId) =>
            !string.IsNullOrWhiteSpace(organizationId)
            && (user.OrganizationIds.Contains(organizationId)
                || user.Roles.ContainsKey(organizationId)
                || user.Permissions.ContainsKey(organizationId));

        private static OrganizationScope Resolve(bool isMultiOrgEnabled, User user, string? requested = null) =>
            OrganizationScopeResolver.Resolve(isMultiOrgEnabled, user, requested, HasAccess);

        // ---------- R0 / H1: single-organization projects ----------

        [Fact] // §4 example 1
        public void MultiOrgDisabled_AlwaysTenantWide_IgnoringRequestAndMemberships()
        {
            var user = new User { OrganizationIds = ["org-a"] };

            var scope = Resolve(isMultiOrgEnabled: false, user, requested: "org-a");

            scope.Kind.Should().Be(OrganizationScopeKind.TenantWide);
            scope.ClaimValue.Should().Be(IdpConstants.DefaultOrganizationId);
        }

        [Fact] // H1 -- even a user with nothing stored must not become "no-org" in single-org mode
        public void MultiOrgDisabled_TenantWide_EvenForAUserWithNoMemberships()
        {
            Resolve(isMultiOrgEnabled: false, new User()).ClaimValue
                .Should().Be(IdpConstants.DefaultOrganizationId);
        }

        // ---------- R2 / H2: the requested organization ----------

        [Fact] // §4 example 3 -- an explicit switch beats the remembered organization
        public void RequestedOrganization_Wins_WhenTheUserBelongsToIt()
        {
            var user = new User
            {
                OrganizationIds = ["org-a", "org-b"],
                LastUsedOrganizationId = "org-b"
            };

            var scope = Resolve(isMultiOrgEnabled: true, user, requested: "org-a");

            scope.Kind.Should().Be(OrganizationScopeKind.Organization);
            scope.ClaimValue.Should().Be("org-a");
        }

        [Fact] // C4 of SPEC9 preserved: membership granted only through a role key still counts
        public void RequestedOrganization_Honoured_WhenGrantedThroughRolesOnly()
        {
            var user = new User
            {
                OrganizationIds = ["default"],
                Roles = new Dictionary<string, List<string>> { ["org-a"] = ["admin"] }
            };

            Resolve(isMultiOrgEnabled: true, user, requested: "org-a").ClaimValue.Should().Be("org-a");
        }

        [Fact] // ...and only through a permission key
        public void RequestedOrganization_Honoured_WhenGrantedThroughPermissionsOnly()
        {
            var user = new User
            {
                OrganizationIds = ["default"],
                Permissions = new Dictionary<string, List<string>> { ["org-a"] = ["read"] }
            };

            Resolve(isMultiOrgEnabled: true, user, requested: "org-a").ClaimValue.Should().Be("org-a");
        }

        // ---------- C1/C2/C3: what a payload can never buy ----------

        [Fact] // §4 example 4, C1
        public void UnauthorisedRequestedOrganization_IsDiscarded_NotEchoed()
        {
            var user = new User { OrganizationIds = ["org-a"] };

            var scope = Resolve(isMultiOrgEnabled: true, user, requested: "org-zzz");

            scope.ClaimValue.Should().Be("org-a");
            scope.ClaimValue.Should().NotBe("org-zzz");
        }

        [Fact] // §4 example 5, C2
        public void RequestedNoOrgSentinel_IsDiscarded()
        {
            var user = new User { OrganizationIds = ["org-a"] };

            Resolve(isMultiOrgEnabled: true, user, requested: IdpConstants.NoOrganizationId)
                .ClaimValue.Should().Be("org-a");
        }

        [Fact] // C3 -- the escalation this whole phase exists to close
        public void RequestedDefault_IsDiscarded_WhenNotExplicitlyGranted()
        {
            var user = new User { OrganizationIds = ["org-a"] };

            var scope = Resolve(isMultiOrgEnabled: true, user, requested: IdpConstants.DefaultOrganizationId);

            scope.Kind.Should().Be(OrganizationScopeKind.Organization);
            scope.ClaimValue.Should().Be("org-a");
        }

        [Fact] // C3 -- but an explicitly granted "default" is still honoured
        public void Default_IsHonoured_WhenItIsLiterallyAMembership()
        {
            var user = new User { OrganizationIds = [IdpConstants.DefaultOrganizationId, "org-a"] };

            Resolve(isMultiOrgEnabled: true, user, requested: IdpConstants.DefaultOrganizationId)
                .ClaimValue.Should().Be(IdpConstants.DefaultOrganizationId);
        }

        [Fact] // C5 -- impersonation: the cloud user matches none of the three tests
        public void ImpersonationStyleUser_WithNoMatchingMembership_ResolvesToDefaultViaItsOwnMembership()
        {
            var cloudUser = new User { OrganizationIds = [IdpConstants.DefaultOrganizationId] };

            Resolve(isMultiOrgEnabled: true, cloudUser, requested: "target-tenant-org")
                .ClaimValue.Should().Be(IdpConstants.DefaultOrganizationId);
        }

        // ---------- R3 / H3: the remembered organization ----------

        [Fact] // §4 example 2
        public void LastUsedOrganization_IsUsed_WhenNothingIsRequested()
        {
            var user = new User
            {
                OrganizationIds = ["org-a", "org-b"],
                LastUsedOrganizationId = "org-b"
            };

            Resolve(isMultiOrgEnabled: true, user).ClaimValue.Should().Be("org-b");
        }

        [Fact] // C4 -- a stale pointer must not survive
        public void LastUsedOrganization_IsIgnored_WhenNoLongerAMember()
        {
            var user = new User
            {
                OrganizationIds = ["org-b"],
                LastUsedOrganizationId = "org-revoked"
            };

            Resolve(isMultiOrgEnabled: true, user).ClaimValue.Should().Be("org-b");
        }

        [Fact]
        public void LastUsedOrganization_IsIgnored_WhenBlank()
        {
            var user = new User { OrganizationIds = ["org-a"], LastUsedOrganizationId = "" };

            Resolve(isMultiOrgEnabled: true, user).ClaimValue.Should().Be("org-a");
        }

        [Fact] // the "no-org" written by a revoke must not be read back as a place
        public void LastUsedOrganization_IsIgnored_WhenItHoldsTheNoOrgSentinel()
        {
            var user = new User
            {
                OrganizationIds = [],
                LastUsedOrganizationId = IdpConstants.NoOrganizationId
            };

            var scope = Resolve(isMultiOrgEnabled: true, user);

            scope.Kind.Should().Be(OrganizationScopeKind.None);
            scope.ClaimValue.Should().Be(IdpConstants.NoOrganizationId);
        }

        // ---------- R4 / R5 / H4: first membership, in preference order ----------

        [Fact]
        public void FirstOrganizationId_IsUsed_WhenNothingRequestedOrRemembered()
        {
            var user = new User { OrganizationIds = ["org-2", "org-3"] };

            Resolve(isMultiOrgEnabled: true, user).ClaimValue.Should().Be("org-2");
        }

        [Fact] // §4 example 7
        public void FirstRoleKey_IsUsed_WhenThereAreNoOrganizationIds()
        {
            var user = new User
            {
                Roles = new Dictionary<string, List<string>> { ["org-c"] = ["admin"] }
            };

            Resolve(isMultiOrgEnabled: true, user).ClaimValue.Should().Be("org-c");
        }

        [Fact]
        public void FirstPermissionKey_IsUsed_WhenThereAreNoOrganizationIdsOrRoles()
        {
            var user = new User
            {
                Permissions = new Dictionary<string, List<string>> { ["org-d"] = ["read"] }
            };

            Resolve(isMultiOrgEnabled: true, user).ClaimValue.Should().Be("org-d");
        }

        [Fact] // H4 -- OrganizationIds is preferred over the dictionaries
        public void OrganizationIds_ArePreferredOverRoleAndPermissionKeys()
        {
            var user = new User
            {
                OrganizationIds = ["org-from-list"],
                Roles = new Dictionary<string, List<string>> { ["org-from-roles"] = ["admin"] },
                Permissions = new Dictionary<string, List<string>> { ["org-from-perms"] = ["read"] }
            };

            Resolve(isMultiOrgEnabled: true, user).ClaimValue.Should().Be("org-from-list");
        }

        // ---------- R6 / H5: belongs to nothing ----------

        [Fact] // §4 example 6 -- previously "default" on OAuth and no claim at all on OIDC
        public void NoMembershipAnywhere_ResolvesToNoOrg()
        {
            var scope = Resolve(isMultiOrgEnabled: true, new User());

            scope.Kind.Should().Be(OrganizationScopeKind.None);
            scope.ClaimValue.Should().Be(IdpConstants.NoOrganizationId);
        }

        [Fact] // §4 example 8, C7 -- blank noise must never become a blank claim
        public void BlankMembershipEntries_AreSkipped_AndNeverEmitted()
        {
            var user = new User
            {
                OrganizationIds = ["", "   "],
                Roles = new Dictionary<string, List<string>> { [" "] = [] }
            };

            var scope = Resolve(isMultiOrgEnabled: true, user);

            scope.Kind.Should().Be(OrganizationScopeKind.None);
            scope.ClaimValue.Should().Be(IdpConstants.NoOrganizationId);
        }

        [Fact]
        public void BlankEntries_AreSkippedInFavourOfARealOne()
        {
            var user = new User { OrganizationIds = ["", "org-real"] };

            Resolve(isMultiOrgEnabled: true, user).ClaimValue.Should().Be("org-real");
        }

        // ---------- totality ----------

        [Theory] // H6 -- no input may produce a null, empty or whitespace claim
        [InlineData(true)]
        [InlineData(false)]
        public void ClaimValue_IsNeverBlank_ForAnyInput(bool isMultiOrgEnabled)
        {
            var users = new[]
            {
                new User(),
                new User { OrganizationIds = [""] },
                new User { OrganizationIds = ["org-a"], LastUsedOrganizationId = "gone" },
                new User { Roles = new Dictionary<string, List<string>> { [""] = [] } }
            };

            var requests = new string?[] { null, "", "   ", "org-a", "no-org", "default", "unknown" };

            foreach (var user in users)
            {
                foreach (var requested in requests)
                {
                    var scope = Resolve(isMultiOrgEnabled, user, requested);
                    scope.ClaimValue.Should().NotBeNullOrWhiteSpace();
                }
            }
        }

        [Fact]
        public void Resolve_Throws_OnNullUser()
        {
            var act = () => OrganizationScopeResolver.Resolve(true, null!, null, HasAccess);

            act.Should().Throw<ArgumentNullException>();
        }

        [Theory]
        [InlineData("no-org", true)]
        [InlineData("default", true)]
        [InlineData("org-a", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsReservedOrganizationId_IdentifiesTheSentinels(string? organizationId, bool expected)
        {
            OrganizationScopeResolver.IsReservedOrganizationId(organizationId).Should().Be(expected);
        }
    }
}
