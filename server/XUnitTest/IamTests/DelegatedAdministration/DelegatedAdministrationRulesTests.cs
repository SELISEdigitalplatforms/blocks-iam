using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.DelegatedAdministration;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.DelegatedAdministration
{
    /// <summary>
    /// R1-R10 as pure functions of a grant scope, over the standard tree:
    /// admin &gt; manager &gt; viewer, standalone auditor (read) and owner (read/write/delete/billing).
    /// </summary>
    public class DelegatedAdministrationRulesTests
    {
        private const string Org = DelegationTestKit.Org;

        private readonly DelegationTestKit _kit = new DelegationTestKit().WithStandardTree();

        private GrantScope ScopeOf(string userId, IEnumerable<string>? roles = null, IEnumerable<string>? permissions = null,
            Dictionary<string, object>? attributes = null)
        {
            var user = _kit.WithUser(userId, roles, permissions, attributes: attributes);
            return GrantScope.Build(new DelegationCaller(userId, Org, false), user, _kit.RolesOf(), _kit.PermissionsOf());
        }

        private static GrantScope SystemScope() =>
            GrantScope.WithoutPower(new DelegationCaller("root-admin", "default", true));

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

            var scope = GrantScope.Build(new DelegationCaller("c", Org, false), user, _kit.RolesOf(), _kit.PermissionsOf());

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

            (DelegatedAdministrationRules.CheckAssignRole(scope, slug) is null).Should().Be(allowed);
        }

        [Fact]
        public void ChildRoleNeedsARoleAboveItEvenWhenItsPermissionsAreHeld()
        {
            var scope = ScopeOf("c", roles: ["manager"]);

            DelegatedAdministrationRules.CheckAssignRole(scope, "manager")!.Reason.Should().Be("managed_role_outside_caller_tree");
            DelegatedAdministrationRules.CheckAssignRole(scope, "viewer").Should().BeNull();
        }

        [Fact]
        public void PeersMayGiveTheirOwnTopLevelRole()
        {
            var scope = ScopeOf("c", roles: ["owner"]);

            DelegatedAdministrationRules.CheckAssignRole(scope, "owner").Should().BeNull();
            DelegatedAdministrationRules.CheckAssignRole(scope, "admin").Should().BeNull("owner holds read/write/delete");
        }

        [Fact]
        public void SeveralRolesAreAUnion()
        {
            var scope = ScopeOf("c", roles: ["auditor"], permissions: ["write", "delete"]);

            DelegatedAdministrationRules.CheckAssignRole(scope, "admin").Should().BeNull();
            DelegatedAdministrationRules.CheckAssignRole(scope, "owner").Should().NotBeNull("billing is not held");
        }

        [Fact]
        public void WithoutAnyTreeEveryRoleIsJudgedOnItsPermissions()
        {
            var kit = new DelegationTestKit().WithRole("a").WithRole("b").WithPermission("x", Org, "a").WithPermission("y", Org, "b");
            var user = kit.WithUser("c", ["a"]);
            var scope = GrantScope.Build(new DelegationCaller("c", Org, false), user, kit.RolesOf(), kit.PermissionsOf());

            DelegatedAdministrationRules.CheckAssignRole(scope, "a").Should().BeNull();
            DelegatedAdministrationRules.CheckAssignRole(scope, "b").Should().NotBeNull();
        }

        // ---------- R2 grant permission ----------

        [Fact]
        public void GrantPermissionNeedsItHeld()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            DelegatedAdministrationRules.CheckGrantPermission(scope, "delete").Should().BeNull();
            DelegatedAdministrationRules.CheckGrantPermission(scope, "billing")!.Rule.Should().Be(DelegatedAdministrationRules.GrantPermission);
        }

        [Fact]
        public void CheckGrantReturnsTheFirstDenialAndSkipsBlanks()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            DelegatedAdministrationRules.CheckGrant(scope, ["viewer", " "], ["read", ""]).Should().BeNull();
            DelegatedAdministrationRules.CheckGrant(scope, ["owner"], ["read"], DelegatedAdministrationRules.SignupLink)!
                .Rule.Should().Be(DelegatedAdministrationRules.SignupLink);
        }

        // ---------- R3 manage user ----------

        [Fact]
        public void ManageUserRefusesSelfNonMembersAndUsersAboveTheCaller()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            DelegatedAdministrationRules.CheckManageUser(scope, _kit.FindUser("c")!)!.Reason.Should().Be("cannot_manage_self");
            DelegatedAdministrationRules.CheckManageUser(scope, _kit.WithUser("outsider", ["viewer"], organizationId: "org-other"))!
                .Reason.Should().Be("user_not_member_of_organization");
            DelegatedAdministrationRules.CheckManageUser(scope, _kit.WithUser("boss", ["owner"]))!
                .Reason.Should().Be("user_holds_role_above_caller");
            DelegatedAdministrationRules.CheckManageUser(scope, _kit.WithUser("direct", ["viewer"], ["billing"]))!
                .Reason.Should().Be("user_holds_permission_above_caller");
            DelegatedAdministrationRules.CheckManageUser(scope, _kit.WithUser("peer", ["auditor"])).Should().BeNull();
        }

        [Fact]
        public void ManageUserIgnoresUnknownRoleSlugsOfTheTarget()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            DelegatedAdministrationRules.CheckManageUser(scope, _kit.WithUser("u", ["viewer", "ghost"])).Should().BeNull();
        }

        [Fact]
        public void ManageUserWithoutMembershipRequirementPassesANonMember()
        {
            var scope = ScopeOf("c", roles: ["admin"]);
            var outsider = _kit.WithUser("outsider", ["owner"], organizationId: "org-other");

            DelegatedAdministrationRules.CheckManageUser(scope, outsider, requireMembership: false).Should().BeNull();
        }

        // ---------- R4 delta ----------

        [Fact]
        public void AccessChangeChecksOnlyTheDelta()
        {
            var scope = ScopeOf("c", roles: ["manager"]);
            var user = _kit.WithUser("u", ["viewer"], ["read"]);

            DelegatedAdministrationRules.CheckAccessChange(scope, user, ["viewer", "auditor"], ["read", "write"]).Should().BeNull();
            DelegatedAdministrationRules.CheckAccessChange(scope, user, ["auditor"], null).Should().BeNull("removing viewer is within the caller's tree");
        }

        [Fact]
        public void AccessChangeRefusesAddingOrRemovingWhatTheCallerCannotGive()
        {
            var scope = ScopeOf("c", roles: ["manager"]);
            var user = _kit.WithUser("u", ["viewer"], ["read"]);

            DelegatedAdministrationRules.CheckAccessChange(scope, user, ["viewer", "owner"], null).Should().NotBeNull();
            DelegatedAdministrationRules.CheckAccessChange(scope, user, null, ["read", "billing"]).Should().NotBeNull();
            DelegatedAdministrationRules.CheckAccessChange(scope, user, ["viewer"], null).Should().BeNull();
        }

        [Fact]
        public void EmptyRequestListsKeepWhatIsStored()
        {
            var (keptAdded, keptRemoved) = DelegatedAdministrationRules.Delta(["viewer"], []);
            keptAdded.Should().BeEmpty();
            keptRemoved.Should().BeEmpty();
            var (added, removed) = DelegatedAdministrationRules.Delta(["viewer", "auditor"], ["viewer", "manager"]);
            added.Should().BeEquivalentTo("manager");
            removed.Should().BeEquivalentTo("auditor");
        }

        [Fact]
        public void RoleDeltaSkipsRemovingARoleTheOrganizationNoLongerKnows()
        {
            var scope = ScopeOf("c", roles: ["manager"]);

            DelegatedAdministrationRules.CheckRoleDelta(scope, [], ["ghost"]).Should().BeNull();
            DelegatedAdministrationRules.CheckRoleDelta(scope, ["ghost"], []).Should().NotBeNull();
            DelegatedAdministrationRules.CheckRoleDelta(scope, [], ["owner"]).Should().NotBeNull();
        }

        // ---------- R5 edit / create role ----------

        [Fact]
        public void EditRoleRefusesOwnRolesAndRolesAboveTheCaller()
        {
            var scope = ScopeOf("c", roles: ["admin"]);
            Role Role(string slug) => _kit.RolesOf().Single(r => r.Slug == slug);

            DelegatedAdministrationRules.CheckEditRole(scope, Role("admin"))!.Reason.Should().Be("cannot_edit_own_role");
            DelegatedAdministrationRules.CheckEditRole(scope, Role("owner")).Should().NotBeNull();
            DelegatedAdministrationRules.CheckEditRole(scope, Role("manager")).Should().BeNull();
        }

        [Fact]
        public void EditRoleNewParentMustBeHeldOrManaged()
        {
            var scope = ScopeOf("c", roles: ["admin"]);
            var viewer = _kit.RolesOf().Single(r => r.Slug == "viewer");

            DelegatedAdministrationRules.CheckEditRole(scope, viewer, "admin", parentChanged: true).Should().BeNull();
            DelegatedAdministrationRules.CheckEditRole(scope, viewer, "auditor", parentChanged: true)!
                .Reason.Should().Be("parent_not_held_or_managed");
            DelegatedAdministrationRules.CheckEditRole(scope, viewer, null, parentChanged: true).Should().BeNull("moving to the top is allowed");
        }

        [Fact]
        public void EditRoleEveryChangedPermissionMustBeHeld()
        {
            var scope = ScopeOf("c", roles: ["admin"]);
            var viewer = _kit.RolesOf().Single(r => r.Slug == "viewer");

            DelegatedAdministrationRules.CheckEditRole(scope, viewer, changedPermissions: ["write"]).Should().BeNull();
            DelegatedAdministrationRules.CheckEditRole(scope, viewer, changedPermissions: ["billing"]).Should().NotBeNull();
        }

        [Fact]
        public void CreateRoleParentMustBeHeldOrManaged()
        {
            var scope = ScopeOf("c", roles: ["manager"]);

            DelegatedAdministrationRules.CheckCreateRole(scope, null).Should().BeNull();
            DelegatedAdministrationRules.CheckCreateRole(scope, "manager").Should().BeNull();
            DelegatedAdministrationRules.CheckCreateRole(scope, "viewer").Should().BeNull();
            DelegatedAdministrationRules.CheckCreateRole(scope, "admin").Should().NotBeNull();
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

            DelegatedAdministrationRules.CreatesCycle(slug, parent, ancestorsOfParent).Should().Be(cycle);
        }

        // ---------- R6 ----------

        [Fact]
        public void SystemOnlyRefusesEveryNonSystemCaller()
        {
            DelegatedAdministrationRules.CheckSystemOnly(ScopeOf("c", roles: ["owner"]), "signup-settings").Should().NotBeNull();
            DelegatedAdministrationRules.CheckSystemOnly(SystemScope(), "signup-settings").Should().BeNull();
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

            DelegatedAdministrationRules.CheckAccessAttributes(scope, "u", null, Access(Org, "region", "eu")).Should().BeNull();
            DelegatedAdministrationRules.CheckAccessAttributes(scope, "u", null, Access(Org, "region", "apac"))!
                .Reason.Should().Be("access_attribute_value_not_held");
            DelegatedAdministrationRules.CheckAccessAttributes(scope, "u", Access(Org, "region", "apac"), new Dictionary<string, object>())!
                .Reason.Should().Be("access_attribute_value_not_held", "removing a value the caller lacks is a change too");
        }

        [Fact]
        public void AccessAttributesOfAnotherOrganizationAreUntouchable()
        {
            var scope = ScopeOf("c", roles: ["admin"], attributes: Access(Org, "region", "eu"));

            DelegatedAdministrationRules.CheckAccessAttributes(scope, "u", null, Access("org-other", "region", "eu"))!
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

            DelegatedAdministrationRules.CheckAccessAttributes(scope, "u", stored, requested).Should().BeNull();
            DelegatedAdministrationRules.CheckAccessAttributes(scope, "u", stored, null).Should().BeNull("the request leaves attributes alone");
        }

        [Fact]
        public void MalformedAccessAttributesAreRefused()
        {
            var scope = ScopeOf("c", roles: ["admin"]);

            DelegatedAdministrationRules.CheckAccessAttributes(scope, "u", null, new Dictionary<string, object> { [AccessAttributes.Key] = "everything" })!
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

            DelegatedAdministrationRules.CheckAssignRole(system, "ghost").Should().BeNull();
            DelegatedAdministrationRules.CheckGrantPermission(system, "anything").Should().BeNull();
            DelegatedAdministrationRules.CheckGrant(system, ["owner"], ["billing"]).Should().BeNull();
            DelegatedAdministrationRules.CheckManageUser(system, user).Should().BeNull();
            DelegatedAdministrationRules.CheckAccessChange(system, user, ["admin"], []).Should().BeNull();
            DelegatedAdministrationRules.CheckRoleDelta(system, ["owner"], ["admin"]).Should().BeNull();
            DelegatedAdministrationRules.CheckCreateRole(system, "owner").Should().BeNull();
            DelegatedAdministrationRules.CheckEditRole(system, owner, "admin", true, ["billing"]).Should().BeNull();
            DelegatedAdministrationRules.CheckAccessAttributes(system, "u", null, Access("any", "x", "y")).Should().BeNull();
        }
    }
}
