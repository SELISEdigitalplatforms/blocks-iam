using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.AccessGrant;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.AccessGrant
{
    /// <summary>
    /// R1-R10 as pure functions of a grant scope, over the standard tree:
    /// admin &gt; manager &gt; viewer, standalone auditor (read) and owner (read/write/delete/billing).
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

        // ---------- GrantScope ----------

        [Fact]
        public void ScopeHoldsRolesManagesTheTreeBelowAndUnionsPermissions()
        {
            var scope = ScopeOf("c", roles: ["manager", "auditor"], permissions: ["direct.export"]);

            scope.HeldRoles.Should().BeEquivalentTo("manager", "auditor");
            scope.ManagedRoles.Should().BeEquivalentTo("viewer");
            scope.HeldPermissions.Should().BeEquivalentTo("read", "write", "direct.export");
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
        }

        // ---------- R1 assign role ----------

        [Theory]
        [InlineData("viewer", true)]   // below a held role, carries only read
        [InlineData("auditor", true)]  // top-level, carries only read
        [InlineData("manager", false)] // held, but a child role needs a role above it
        [InlineData("admin", false)]   // top-level, carries delete
        [InlineData("owner", false)]   // top-level, carries billing
        [InlineData("ghost", false)]   // unknown in the organization
        public void AssignRoleFollowsTreeAndPermissions(string slug, bool allowed)
        {
            var scope = ScopeOf("c", roles: ["manager", "auditor"]);

            (AccessGrantRules.CheckAssignRole(scope, slug) is null).Should().Be(allowed);
        }

        [Fact]
        public void ChildRoleNeedsARoleAboveItEvenWhenItsPermissionsAreHeld()
        {
            var scope = ScopeOf("c", roles: ["manager"]);

            AccessGrantRules.CheckAssignRole(scope, "manager")!.Reason.Should().Be("managed_role_outside_caller_tree");
            AccessGrantRules.CheckAssignRole(scope, "viewer").Should().BeNull();
        }

        [Fact]
        public void PeersMayGiveTheirOwnTopLevelRole()
        {
            var scope = ScopeOf("c", roles: ["owner"]);

            AccessGrantRules.CheckAssignRole(scope, "owner").Should().BeNull();
            AccessGrantRules.CheckAssignRole(scope, "admin").Should().BeNull("owner holds read/write/delete");
        }

        [Fact]
        public void SeveralRolesAreAUnion()
        {
            var scope = ScopeOf("c", roles: ["auditor"], permissions: ["write", "delete"]);

            AccessGrantRules.CheckAssignRole(scope, "admin").Should().BeNull();
            AccessGrantRules.CheckAssignRole(scope, "owner").Should().NotBeNull("billing is not held");
        }

        [Fact]
        public void WithoutAnyTreeEveryRoleIsJudgedOnItsPermissions()
        {
            var kit = new AccessGrantTestKit().WithRole("a").WithRole("b").WithPermission("x", Org, "a").WithPermission("y", Org, "b");
            var user = kit.WithUser("c", ["a"]);
            var scope = GrantScope.Build(new GrantCaller("c", Org, false), user, kit.RolesOf(), kit.PermissionsOf());

            AccessGrantRules.CheckAssignRole(scope, "a").Should().BeNull();
            AccessGrantRules.CheckAssignRole(scope, "b").Should().NotBeNull();
        }

        // ---------- R2 grant permission ----------

        [Fact]
        public void GrantPermissionNeedsItHeld()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            AccessGrantRules.CheckGrantPermission(scope, "delete").Should().BeNull();
            AccessGrantRules.CheckGrantPermission(scope, "billing")!.Rule.Should().Be(AccessGrantRules.GrantPermission);
        }

        [Fact]
        public void CheckGrantReturnsTheFirstDenialAndSkipsBlanks()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            AccessGrantRules.CheckGrant(scope, ["viewer", " "], ["read", ""]).Should().BeNull();
            AccessGrantRules.CheckGrant(scope, ["owner"], ["read"], AccessGrantRules.SignupLink)!
                .Rule.Should().Be(AccessGrantRules.SignupLink);
        }

        // ---------- R3 manage user ----------

        [Fact]
        public void ManageUserRefusesSelfNonMembersAndUsersAboveTheCaller()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            AccessGrantRules.CheckManageUser(scope, _kit.FindUser("c")!)!.Reason.Should().Be("cannot_manage_self");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("outsider", ["viewer"], organizationId: "org-other"))!
                .Reason.Should().Be("user_not_member_of_organization");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("boss", ["owner"]))!
                .Reason.Should().Be("user_holds_role_above_caller");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("direct", ["viewer"], ["billing"]))!
                .Reason.Should().Be("user_holds_permission_above_caller");
            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("peer", ["auditor"])).Should().BeNull();
        }

        [Fact]
        public void ManageUserIgnoresUnknownRoleSlugsOfTheTarget()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            AccessGrantRules.CheckManageUser(scope, _kit.WithUser("u", ["viewer", "ghost"])).Should().BeNull();
        }

        [Fact]
        public void ManageUserWithoutMembershipRequirementPassesANonMember()
        {
            var scope = ScopeOf("c", roles: ["admin"]);
            var outsider = _kit.WithUser("outsider", ["owner"], organizationId: "org-other");

            AccessGrantRules.CheckManageUser(scope, outsider, requireMembership: false).Should().BeNull();
        }

        // ---------- R4 delta ----------

        [Fact]
        public void AccessChangeChecksOnlyTheDelta()
        {
            var scope = ScopeOf("c", roles: ["manager"]);
            var user = _kit.WithUser("u", ["viewer"], ["read"]);

            AccessGrantRules.CheckAccessChange(scope, user, ["viewer", "auditor"], ["read", "write"]).Should().BeNull();
            AccessGrantRules.CheckAccessChange(scope, user, ["auditor"], null).Should().BeNull("removing viewer is within the caller's tree");
        }

        [Fact]
        public void AccessChangeRefusesAddingOrRemovingWhatTheCallerCannotGive()
        {
            var scope = ScopeOf("c", roles: ["manager"]);
            var user = _kit.WithUser("u", ["viewer"], ["read"]);

            AccessGrantRules.CheckAccessChange(scope, user, ["viewer", "owner"], null).Should().NotBeNull();
            AccessGrantRules.CheckAccessChange(scope, user, null, ["read", "billing"]).Should().NotBeNull();
            AccessGrantRules.CheckAccessChange(scope, user, ["viewer"], null).Should().BeNull();
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
            var scope = ScopeOf("c", roles: ["manager"]);

            AccessGrantRules.CheckRoleDelta(scope, [], ["ghost"]).Should().BeNull();
            AccessGrantRules.CheckRoleDelta(scope, ["ghost"], []).Should().NotBeNull();
            AccessGrantRules.CheckRoleDelta(scope, [], ["owner"]).Should().NotBeNull();
        }

        // ---------- R5 edit / create role ----------

        [Fact]
        public void EditRoleRefusesOwnRolesAndRolesAboveTheCaller()
        {
            var scope = ScopeOf("c", roles: ["admin"]);
            Role Role(string slug) => _kit.RolesOf().Single(r => r.Slug == slug);

            AccessGrantRules.CheckEditRole(scope, Role("admin"))!.Reason.Should().Be("cannot_edit_own_role");
            AccessGrantRules.CheckEditRole(scope, Role("owner")).Should().NotBeNull();
            AccessGrantRules.CheckEditRole(scope, Role("manager")).Should().BeNull();
        }

        [Fact]
        public void EditRoleNewParentMustBeHeldOrManaged()
        {
            var scope = ScopeOf("c", roles: ["admin"]);
            var viewer = _kit.RolesOf().Single(r => r.Slug == "viewer");

            AccessGrantRules.CheckEditRole(scope, viewer, "admin", parentChanged: true).Should().BeNull();
            AccessGrantRules.CheckEditRole(scope, viewer, "auditor", parentChanged: true)!
                .Reason.Should().Be("parent_not_held_or_managed");
            AccessGrantRules.CheckEditRole(scope, viewer, null, parentChanged: true).Should().BeNull("moving to the top is allowed");
        }

        [Fact]
        public void EditRoleEveryChangedPermissionMustBeHeld()
        {
            var scope = ScopeOf("c", roles: ["admin"]);
            var viewer = _kit.RolesOf().Single(r => r.Slug == "viewer");

            AccessGrantRules.CheckEditRole(scope, viewer, changedPermissions: ["write"]).Should().BeNull();
            AccessGrantRules.CheckEditRole(scope, viewer, changedPermissions: ["billing"]).Should().NotBeNull();
        }

        [Fact]
        public void CreateRoleParentMustBeHeldOrManaged()
        {
            var scope = ScopeOf("c", roles: ["manager"]);

            AccessGrantRules.CheckCreateRole(scope, null).Should().BeNull();
            AccessGrantRules.CheckCreateRole(scope, "manager").Should().BeNull();
            AccessGrantRules.CheckCreateRole(scope, "viewer").Should().BeNull();
            AccessGrantRules.CheckCreateRole(scope, "admin").Should().NotBeNull();
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
