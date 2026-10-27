using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.AccessGrant;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.AccessGrant
{
    /// <summary>
    /// R1-R10 as pure functions of a grant scope, over the standard tree:
    /// admin &gt; manager &gt; viewer, standalone auditor (read) and owner (read/write/delete/billing).
    /// admin and manager carry the manage permissions: an admin holder is a top user, a manager
    /// holder a descendant.
    /// </summary>
    public class AccessGrantRulesTests
    {
        private const string Org = AccessGrantTestKit.Org;

        private readonly AccessGrantTestKit _kit = new AccessGrantTestKit().WithStandardTree();

        private GrantScope ScopeOf(string userId, IEnumerable<string>? roles = null, IEnumerable<string>? permissions = null,
            Dictionary<string, object>? attributes = null)
        {
            var user = _kit.WithUser(userId, roles, permissions, attributes: attributes);
            return GrantScope.Build(new GrantCaller(userId, Org, false), user, _kit.RolesOf(), _kit.PermissionsOf());
        }

        private static GrantScope SystemScope() =>
            GrantScope.WithoutPower(new GrantCaller("root-admin", "default", true));

        private Role RoleOf(string slug) => _kit.RolesOf().Single(r => r.Slug == slug);

        // ---------- GrantScope ----------

        [Fact]
        public void ScopeHoldsRolesManagesTheTreeBelowAndUnionsPermissions()
        {
            var scope = ScopeOf("c", roles: ["manager", "auditor"], permissions: ["direct.export"]);

            scope.HeldRoles.Should().BeEquivalentTo("manager", "auditor");
            scope.ManagedRoles.Should().BeEquivalentTo("viewer");
            scope.HeldPermissions.Should().BeEquivalentTo(
                "read", "write", "direct.export", AccessGrantRules.MutateRolesPermission, AccessGrantRules.MutateUsersPermission);
            scope.OwnPermissions.Should().BeEquivalentTo(scope.HeldPermissions);
        }

        [Fact]
        public void OwnPermissionsLeaveOutWhatOnlyARoleBelowCarries()
        {
            _kit.WithPermission("export", Org, "viewer");

            var scope = ScopeOf("c", roles: ["manager"]);

            scope.HeldPermissions.Should().Contain("export");
            scope.OwnPermissions.Should().NotContain("export");
        }

        [Theory]
        [InlineData("admin", true)]    // standalone and carries the manage permissions
        [InlineData("manager", false)] // carries them, but has a parent: a descendant
        [InlineData("owner", false)]   // standalone, but no manage permission
        [InlineData("auditor", false)]
        public void TopUserGetsManagePowerFromAStandaloneRole(string role, bool isTop)
        {
            ScopeOf("c", roles: [role]).IsTop.Should().Be(isTop);
        }

        [Fact]
        public void ADirectManagePermissionMakesATopUser()
        {
            ScopeOf("c", roles: ["viewer"], permissions: [AccessGrantRules.MutateUsersPermission]).IsTop.Should().BeTrue();
        }

        [Fact]
        public void ABaseRoleNextToAChildManageRoleIsStillADescendant()
        {
            ScopeOf("c", roles: ["auditor", "manager"]).IsTop.Should().BeFalse();
        }

        [Fact]
        public void ARoleBelowABaseRoleIsNotBelowItsHolders()
        {
            _kit.WithRole("parked", "auditor").WithPermission("read", Org, "parked");
            var scope = ScopeOf("d", roles: ["manager", "auditor"]);

            scope.ManagedRoles.Should().BeEquivalentTo("viewer");
            AccessGrantRules.CheckAssignRole(scope, "parked")!.Reason.Should().Be("role_not_below_caller");
            AccessGrantRules.CheckCreateRole(scope, "auditor")!.Reason.Should().Be("parent_not_held_or_managed");
            AccessGrantRules.CheckEditRole(scope, RoleOf("viewer"), "auditor", parentChanged: true)!
                .Reason.Should().Be("parent_not_held_or_managed");
        }

        [Fact]
        public void ScopeIgnoresUnknownAndArchivedHeldRoles()
        {
            _kit.RolesOf().Add(new Role { Slug = "retired", Name = "retired", OrganizationId = Org, IsArchived = true });
            _kit.WithPermission("secret", Org, "retired", "ghost");

            var scope = ScopeOf("c", roles: ["viewer", "retired", "ghost"]);

            scope.HeldRoles.Should().BeEquivalentTo("viewer");
            scope.HeldPermissions.Should().BeEquivalentTo("read");
        }

        [Fact]
        public void ScopeOfAnotherOrganizationsMemberHoldsNothingHere()
        {
            var user = _kit.WithUser("c", ["admin"], organizationId: "org-other");

            var scope = GrantScope.Build(new GrantCaller("c", Org, false), user, _kit.RolesOf(), _kit.PermissionsOf());

            scope.HeldRoles.Should().BeEmpty();
            scope.HeldPermissions.Should().BeEmpty();
            scope.IsTop.Should().BeFalse();
        }

        // ---------- R1 assign role ----------

        [Theory]
        [InlineData("viewer", true)]
        [InlineData("manager", true)]
        [InlineData("admin", true)]
        [InlineData("owner", true)]
        [InlineData("auditor", true)]
        [InlineData("ghost", false)]   // unknown in the organization
        public void TopUserMayGiveEveryLiveRole(string slug, bool allowed)
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            (AccessGrantRules.CheckAssignRole(scope, slug) is null).Should().Be(allowed);
        }

        [Theory]
        [InlineData("viewer", true)]   // below the held role
        [InlineData("manager", false)] // its own role: a peer, not below
        [InlineData("admin", false)]   // above
        [InlineData("auditor", false)] // standalone
        [InlineData("owner", false)]
        public void DescendantMayGiveOnlyRolesBelowIt(string slug, bool allowed)
        {
            var scope = ScopeOf("c", roles: ["manager"]);

            (AccessGrantRules.CheckAssignRole(scope, slug) is null).Should().Be(allowed);
        }

        [Fact]
        public void DescendantMayGiveARoleBelowItThatCarriesMoreThanItHolds()
        {
            // The tree decides: whoever built it put this feature role under the caller.
            _kit.WithRole("doctor", "manager").WithPermission("medical", Org, "doctor");
            var scope = ScopeOf("c", roles: ["manager"]);

            AccessGrantRules.CheckAssignRole(scope, "doctor").Should().BeNull();
        }

        [Fact]
        public void ACopyFromDefaultMayBeGivenByATopUser()
        {
            _kit.RolesOf().Add(new Role { Slug = "member", Name = "member", OrganizationId = Org, CreatedFromDefault = true });

            AccessGrantRules.CheckAssignRole(ScopeOf("c", roles: ["admin"]), "member").Should().BeNull();
            AccessGrantRules.CheckAssignRole(ScopeOf("d", roles: ["manager"]), "member")!.Reason.Should().Be("role_not_below_caller");
        }

        // ---------- R2 grant permission ----------

        [Fact]
        public void OnlyATopUserGivesDirectPermissions()
        {
            AccessGrantRules.CheckGrantPermission(ScopeOf("c", roles: ["admin"]), "billing").Should().BeNull();
            AccessGrantRules.CheckGrantPermission(ScopeOf("d", roles: ["manager"]), "read")!
                .Reason.Should().Be("descendant_cannot_grant_direct_permission");
            AccessGrantRules.CheckGrantPermission(ScopeOf("c", roles: ["admin"]), " ")!.Reason.Should().Be("permission_empty");
        }

        [Fact]
        public void ADescendantPutsOnARoleOnlyWhatItsOwnRolesCarry()
        {
            var descendant = ScopeOf("d", roles: ["manager"]);

            AccessGrantRules.CheckRolePermission(descendant, "write").Should().BeNull();
            AccessGrantRules.CheckRolePermission(descendant, "delete")!.Reason.Should().Be("permission_not_on_own_role");
            AccessGrantRules.CheckRolePermission(ScopeOf("c", roles: ["admin"]), "billing").Should().BeNull();
        }

        [Fact]
        public void CheckGrantReturnsTheFirstDenialAndSkipsBlanks()
        {
            AccessGrantRules.CheckGrant(ScopeOf("c", roles: ["admin"]), ["viewer", " "], ["read", ""]).Should().BeNull();
            AccessGrantRules.CheckGrant(ScopeOf("d", roles: ["manager"]), ["viewer"], ["read"], AccessGrantRules.SignupLink)!
                .Rule.Should().Be(AccessGrantRules.SignupLink);
            AccessGrantRules.CheckGrant(ScopeOf("d", roles: ["manager"]), ["viewer", " "], [""]).Should().BeNull();
        }

        // ---------- R3 manage user ----------

        [Fact]
        public void TopUserManagesEveryMemberButNotItself()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            AccessGrantRules.CheckManageUser(scope, _kit.FindUser("c")!)!.Reason.Should().Be("cannot_manage_self");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("outsider", ["viewer"], organizationId: "org-other"))!
                .Reason.Should().Be("user_not_member_of_organization");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("boss", ["owner"], ["billing"])).Should().BeNull();
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("peer", ["admin"])).Should().BeNull();
        }

        [Fact]
        public void DescendantManagesOnlyUsersBelowIt()
        {
            var scope = ScopeOf("d", roles: ["manager", "auditor"]);

            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("below", ["viewer"])).Should().BeNull();
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("base", ["viewer", "auditor"]))
                .Should().BeNull("a base role the caller holds too gives no power to manage");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("peer", ["manager"]))!
                .Reason.Should().Be("holds_role_not_below_caller");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("boss", ["admin"]))!
                .Reason.Should().Be("holds_role_not_below_caller");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("side", ["owner"]))!
                .Reason.Should().Be("holds_role_not_below_caller");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("direct", ["viewer"], ["billing"]))!
                .Reason.Should().Be("holds_permission_above_caller");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("direct-own", ["viewer"], ["read"])).Should().BeNull();
        }

        [Fact]
        public void ManageUserIgnoresUnknownRoleSlugsOfTheTarget()
        {
            var scope = ScopeOf("d", roles: ["manager"]);

            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("u", ["viewer", "ghost"])).Should().BeNull();
        }

        [Fact]
        public void ManageUserWithoutMembershipRequirementPassesANonMember()
        {
            var scope = ScopeOf("d", roles: ["manager"]);
            var outsider = _kit.WithUser("outsider", ["owner"], organizationId: "org-other");

            AccessGrantRules.CheckManageUser(scope, outsider, requireMembership: false).Should().BeNull();
        }

        // ---------- R4 delta ----------

        [Fact]
        public void AccessChangeChecksOnlyTheDelta()
        {
            var scope = ScopeOf("d", roles: ["manager"]);
            var user = _kit.WithUser("u", ["viewer"], ["read"]);

            AccessGrantRules.CheckAccessChange(scope, user, ["viewer"], ["read"]).Should().BeNull("nothing changes");
            AccessGrantRules.CheckAccessChange(scope, user, null, null).Should().BeNull();
        }

        [Fact]
        public void AccessChangeRefusesAddingOrRemovingWhatTheCallerCannotGive()
        {
            var scope = ScopeOf("d", roles: ["manager"]);
            var user = _kit.WithUser("u", ["viewer"], ["read"]);

            AccessGrantRules.CheckAccessChange(scope, user, ["viewer", "owner"], null).Should().NotBeNull();
            AccessGrantRules.CheckAccessChange(scope, user, null, ["read", "write"])!
                .Reason.Should().Be("descendant_cannot_grant_direct_permission");
            AccessGrantRules.CheckAccessChange(ScopeOf("c", roles: ["admin"]), user, ["owner"], ["billing"]).Should().BeNull();
        }

        [Fact]
        public void EmptyRequestListsKeepWhatIsStored()
        {
            var (keptAdded, keptRemoved) = AccessGrantRules.Delta(["viewer"], []);
            keptAdded.Should().BeEmpty();
            keptRemoved.Should().BeEmpty();
            var (added, removed) = AccessGrantRules.Delta(["viewer", "auditor"], ["viewer", "manager"]);
            added.Should().BeEquivalentTo("manager");
            removed.Should().BeEquivalentTo("auditor");
        }

        [Fact]
        public void RoleDeltaSkipsRemovingARoleTheOrganizationNoLongerKnows()
        {
            var scope = ScopeOf("d", roles: ["manager"]);

            AccessGrantRules.CheckRoleDelta(scope, [], ["ghost"]).Should().BeNull();
            AccessGrantRules.CheckRoleDelta(scope, ["ghost"], []).Should().NotBeNull();
            AccessGrantRules.CheckRoleDelta(scope, [], ["owner"]).Should().NotBeNull();
        }

        // ---------- R5 edit / create role ----------

        [Fact]
        public void TopUserEditsEveryRoleOfTheOrganizationExceptItsOwnAndCopies()
        {
            var scope = ScopeOf("c", roles: ["admin"]);
            _kit.RolesOf().Add(new Role { Slug = "member", Name = "member", OrganizationId = Org, CreatedFromDefault = true });

            AccessGrantRules.CheckEditRole(scope, RoleOf("admin"))!.Reason.Should().Be("cannot_edit_own_role");
            AccessGrantRules.CheckEditRole(scope, RoleOf("member"))!.Reason.Should().Be("default_copy_is_read_only");
            AccessGrantRules.CheckEditRole(scope, RoleOf("owner")).Should().BeNull();
            AccessGrantRules.CheckEditRole(scope, RoleOf("manager"), "auditor", parentChanged: true, ["billing"]).Should().BeNull();
            AccessGrantRules.CheckEditRole(scope, RoleOf("viewer"), null, parentChanged: true).Should().BeNull("a top user may make a role standalone");
        }

        [Fact]
        public void DescendantEditsOnlyRolesBelowIt()
        {
            var scope = ScopeOf("d", roles: ["manager"]);

            AccessGrantRules.CheckEditRole(scope, RoleOf("manager"))!.Reason.Should().Be("cannot_edit_own_role");
            AccessGrantRules.CheckEditRole(scope, RoleOf("admin"))!.Reason.Should().Be("role_not_below_caller");
            AccessGrantRules.CheckEditRole(scope, RoleOf("owner"))!.Reason.Should().Be("role_not_below_caller");
            AccessGrantRules.CheckEditRole(scope, RoleOf("viewer")).Should().BeNull();
        }

        [Fact]
        public void DescendantKeepsAMovedRoleBelowIt()
        {
            var scope = ScopeOf("d", roles: ["manager"]);
            var viewer = RoleOf("viewer");

            AccessGrantRules.CheckEditRole(scope, viewer, "manager", parentChanged: true).Should().BeNull();
            AccessGrantRules.CheckEditRole(scope, viewer, "auditor", parentChanged: true)!
                .Reason.Should().Be("parent_not_held_or_managed");
            AccessGrantRules.CheckEditRole(scope, viewer, null, parentChanged: true)!
                .Reason.Should().Be("descendant_cannot_move_role_to_top");
        }

        [Fact]
        public void DescendantAddsOnlyPermissionsItsOwnRolesCarry()
        {
            var scope = ScopeOf("d", roles: ["manager"]);
            var viewer = RoleOf("viewer");

            AccessGrantRules.CheckEditRole(scope, viewer, addedPermissions: ["write"]).Should().BeNull();
            AccessGrantRules.CheckEditRole(scope, viewer, addedPermissions: ["delete"])!.Reason.Should().Be("permission_not_on_own_role");
        }

        [Fact]
        public void TopUserCreatesAStandaloneRoleOrOneUnderAnyLiveRole()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            AccessGrantRules.CheckCreateRole(scope, null).Should().BeNull();
            AccessGrantRules.CheckCreateRole(scope, "owner").Should().BeNull();
            AccessGrantRules.CheckCreateRole(scope, "ghost")!.Reason.Should().Be("parent_not_found_or_archived");
        }

        [Fact]
        public void DescendantCreatesOnlyBelowItsOwnRole()
        {
            var scope = ScopeOf("d", roles: ["manager"]);

            AccessGrantRules.CheckCreateRole(scope, null)!.Reason.Should().Be("descendant_must_create_below_own_role");
            AccessGrantRules.CheckCreateRole(scope, "manager").Should().BeNull();
            AccessGrantRules.CheckCreateRole(scope, "viewer").Should().BeNull();
            AccessGrantRules.CheckCreateRole(scope, "admin")!.Reason.Should().Be("parent_not_held_or_managed");
        }

        [Theory]
        [InlineData("viewer", "viewer", true)]
        [InlineData("admin", "viewer", true)]
        [InlineData("viewer", "admin", false)]
        [InlineData("viewer", null, false)]
        public void CycleWhenTheNewParentIsTheRoleOrBelowIt(string slug, string? parent, bool cycle)
        {
            var ancestorsOfParent = parent is null
                ? new List<string>()
                : new List<string> { parent }.Concat(_kit.RolesOf().Single(r => r.Slug == parent).AncestorRoleSlugs).ToList();

            AccessGrantRules.CreatesCycle(slug, parent, ancestorsOfParent).Should().Be(cycle);
        }

        // ---------- R6 ----------

        [Fact]
        public void SystemOnlyRefusesEveryNonSystemCaller()
        {
            AccessGrantRules.CheckSystemOnly(ScopeOf("c", roles: ["owner"]), "signup-settings").Should().NotBeNull();
            AccessGrantRules.CheckSystemOnly(SystemScope(), "signup-settings").Should().BeNull();
        }

        // ---------- R10 access attributes ----------

        private static Dictionary<string, object> Access(string org, string name, params string[] values) =>
            new()
            {
                [AccessAttributes.Key] = new Dictionary<string, object>
                {
                    [org] = new Dictionary<string, object> { [name] = values.Cast<object>().ToList() }
                }
            };

        [Fact]
        public void AccessAttributesOnlyFromTheCallersOwnValues()
        {
            var scope = ScopeOf("c", roles: ["admin"], attributes: Access(Org, "region", "eu", "us"));

            AccessGrantRules.CheckAccessAttributes(scope, "u", null, Access(Org, "region", "eu")).Should().BeNull();
            AccessGrantRules.CheckAccessAttributes(scope, "u", null, Access(Org, "region", "apac"))!
                .Reason.Should().Be("access_attribute_value_not_held");
            AccessGrantRules.CheckAccessAttributes(scope, "u", Access(Org, "region", "apac"), new Dictionary<string, object>())!
                .Reason.Should().Be("access_attribute_value_not_held", "removing a value the caller lacks is a change too");
        }

        [Fact]
        public void AccessAttributesOfAnotherOrganizationAreUntouchable()
        {
            var scope = ScopeOf("c", roles: ["admin"], attributes: Access(Org, "region", "eu"));

            AccessGrantRules.CheckAccessAttributes(scope, "u", null, Access("org-other", "region", "eu"))!
                .Reason.Should().Be("access_attributes_of_other_organization");
        }

        [Fact]
        public void OtherAttributeKeysAndUnchangedValuesAreNotChecked()
        {
            var scope = ScopeOf("c", roles: ["admin"]);
            var stored = Access(Org, "region", "apac");
            stored["plan"] = "gold";
            var requested = Access(Org, "region", "apac");
            requested["plan"] = "platinum";

            AccessGrantRules.CheckAccessAttributes(scope, "u", stored, requested).Should().BeNull();
            AccessGrantRules.CheckAccessAttributes(scope, "u", stored, null).Should().BeNull("the request leaves attributes alone");
        }

        [Fact]
        public void MalformedAccessAttributesAreRefused()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            AccessGrantRules.CheckAccessAttributes(scope, "u", null, new Dictionary<string, object> { [AccessAttributes.Key] = "everything" })!
                .Reason.Should().Be("access_attributes_malformed");
        }

        [Fact]
        public void AccessAttributesReadFromJsonAndSingleStrings()
        {
            var bag = new Dictionary<string, object>
            {
                [AccessAttributes.Key] = System.Text.Json.JsonDocument.Parse("{\"" + Org + "\":{\"region\":[\"eu\"],\"team\":\"blue\"}}").RootElement
            };

            var read = AccessAttributes.Read(bag);

            read[Org]["region"].Should().BeEquivalentTo("eu");
            read[Org]["team"].Should().BeEquivalentTo("blue");
        }

        // ---------- system user: no rule applies ----------

        [Fact]
        public void TheSystemUserPassesEveryRule()
        {
            var system = SystemScope();
            var user = _kit.WithUser("u", ["owner"], ["billing"]);
            var owner = _kit.RolesOf().Single(r => r.Slug == "owner");

            AccessGrantRules.CheckAssignRole(system, "ghost").Should().BeNull();
            AccessGrantRules.CheckGrantPermission(system, "anything").Should().BeNull();
            AccessGrantRules.CheckGrant(system, ["owner"], ["billing"]).Should().BeNull();
            AccessGrantRules.CheckManageUser(system, user).Should().BeNull();
            AccessGrantRules.CheckAccessChange(system, user, ["admin"], []).Should().BeNull();
            AccessGrantRules.CheckRoleDelta(system, ["owner"], ["admin"]).Should().BeNull();
            AccessGrantRules.CheckCreateRole(system, "owner").Should().BeNull();
            AccessGrantRules.CheckEditRole(system, owner, "admin", true, ["billing"]).Should().BeNull();
            AccessGrantRules.CheckAccessAttributes(system, "u", null, Access("any", "x", "y")).Should().BeNull();
        }
    }
}
