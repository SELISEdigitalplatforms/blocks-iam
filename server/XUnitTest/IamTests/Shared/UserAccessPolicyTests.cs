using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Utilities;

namespace XUnitTest.IamTests.Shared
{
    /// <summary>
    /// The rules behind user access lists and role grants. Pure, so each case is a caller, a target
    /// and an answer.
    /// </summary>
    public class UserAccessPolicyTests
    {
        private const string Org = "firm-a";

        // admin -> senior-lawyer -> lawyer -> client, plus a standalone auditor.
        private static readonly List<Role> Hierarchy =
        [
            new() { Slug = "admin", CanCreateOwn = true, AncestorRoleSlugs = [] },
            new() { Slug = "senior-lawyer", CanCreateOwn = true, ParentRoleSlug = "admin", AncestorRoleSlugs = ["admin"] },
            new() { Slug = "lawyer", CanCreateOwn = true, ParentRoleSlug = "senior-lawyer", AncestorRoleSlugs = ["senior-lawyer", "admin"] },
            new() { Slug = "client", CanCreateOwn = false, ParentRoleSlug = "lawyer", AncestorRoleSlugs = ["lawyer", "senior-lawyer", "admin"] },
            new() { Slug = "auditor", CanCreateOwn = false, AncestorRoleSlugs = [] }
        ];

        // No parents anywhere. CanCreateOwn true on one role, as legacy documents read it.
        private static readonly List<Role> Flat =
        [
            new() { Slug = "admin", CanCreateOwn = true, AncestorRoleSlugs = [] },
            new() { Slug = "member", CanCreateOwn = false, AncestorRoleSlugs = [] }
        ];

        private static CallerAccess User(string userId, IEnumerable<Role> orgRoles, bool multiOrg = true, params string[] roles) => new()
        {
            Kind = CallerKind.User,
            OrganizationId = Org,
            UserId = userId,
            Roles = roles.ToHashSet(StringComparer.OrdinalIgnoreCase),
            OrganizationRoles = orgRoles.ToList(),
            IsMultiOrgEnabled = multiOrg,
            UsesHierarchy = UserAccessPolicy.UsesHierarchy(orgRoles),
            IsTopRoleHolder = UserAccessPolicy.TopRoles(orgRoles).Any(r => roles.Contains(r.Slug))
        };

        private static AccessSubject Member(string id, string? createdBy = null, UserAccessList? manage = null, UserAccessList? view = null, params string[] roles) => new(
            id,
            [Org],
            new Dictionary<string, List<string>> { [Org] = roles.ToList() },
            new Dictionary<string, List<string>>(),
            view is null ? new Dictionary<string, UserAccessList>() : new() { [Org] = view },
            manage is null ? new Dictionary<string, UserAccessList>() : new() { [Org] = manage },
            createdBy);

        [Fact]
        public void FlatOrganization_EveryCallerReachesEveryMember_AsBefore()
        {
            UserAccessPolicy.UsesHierarchy(Flat).Should().BeFalse();

            var caller = User("lina", Flat, true, "member");
            var target = Member("c2", createdBy: "leo");

            UserAccessPolicy.Evaluate(caller, target).Should().Be(UserAccessLevel.Manage);
            UserAccessPolicy.RequiresAccessListFilter(caller).Should().BeFalse();
        }

        [Fact]
        public void Hierarchy_LawyerReachesOnlyTheClientsListedForThem()
        {
            var lina = User("lina", Hierarchy, true, "lawyer");

            UserAccessPolicy.Evaluate(lina, Member("c1", manage: new UserAccessList { Users = ["lina"] }))
                .Should().Be(UserAccessLevel.Manage);
            UserAccessPolicy.Evaluate(lina, Member("c2", manage: new UserAccessList { Users = ["leo"] }))
                .Should().Be(UserAccessLevel.None);
            UserAccessPolicy.Evaluate(lina, Member("c3", manage: new UserAccessList(), view: new UserAccessList { Users = ["lina"] }))
                .Should().Be(UserAccessLevel.View);
        }

        [Fact]
        public void Hierarchy_RoleEntryReachesEveryHolder_AndTopRoleReachesEveryone()
        {
            var sam = User("sam", Hierarchy, true, "senior-lawyer");
            var asha = User("asha", Hierarchy, true, "admin");
            var c1 = Member("c1", manage: new UserAccessList { Users = ["lina"], Roles = ["senior-lawyer"] });
            var unlisted = Member("c9", manage: new UserAccessList());

            UserAccessPolicy.Evaluate(sam, c1).Should().Be(UserAccessLevel.Manage);
            UserAccessPolicy.Evaluate(sam, unlisted).Should().Be(UserAccessLevel.None);
            UserAccessPolicy.Evaluate(asha, unlisted).Should().Be(UserAccessLevel.Manage);
        }

        [Fact]
        public void Hierarchy_NoStoredList_FallsBackToTheCreator()
        {
            var lina = User("lina", Hierarchy, true, "lawyer");

            UserAccessPolicy.Evaluate(lina, Member("legacy", createdBy: "lina")).Should().Be(UserAccessLevel.Manage);
            UserAccessPolicy.Evaluate(lina, Member("legacy", createdBy: "leo")).Should().Be(UserAccessLevel.None);
        }

        [Fact]
        public void NonMemberOfTheCallersOrganization_IsNeverReached()
        {
            var caller = User("lina", Flat, true, "admin");
            var outsider = new AccessSubject("x", ["firm-b"], new Dictionary<string, List<string>>(), new Dictionary<string, List<string>>(),
                new Dictionary<string, UserAccessList>(), new Dictionary<string, UserAccessList>());

            UserAccessPolicy.Evaluate(caller, outsider).Should().Be(UserAccessLevel.None);
        }

        [Fact]
        public void SingleOrganizationTenant_TreatsEveryAccountAsAMember()
        {
            var caller = User("lina", Flat, multiOrg: false, "admin");
            var noMembershipStored = new AccessSubject("old", [], new Dictionary<string, List<string>>(), new Dictionary<string, List<string>>(),
                new Dictionary<string, UserAccessList>(), new Dictionary<string, UserAccessList>());

            UserAccessPolicy.Evaluate(caller, noMembershipStored).Should().Be(UserAccessLevel.Manage);
        }

        [Fact]
        public void Grant_FollowsTheTree()
        {
            var lina = User("lina", Hierarchy, true, "lawyer");
            UserAccessPolicy.FindUngrantableRole(lina, ["client"], new Dictionary<string, IReadOnlySet<string>>()).Should().BeNull();
            UserAccessPolicy.FindUngrantableRole(lina, ["admin"], new Dictionary<string, IReadOnlySet<string>>()).Should().Be("admin");
            UserAccessPolicy.FindUngrantableRole(lina, ["superadmin"], new Dictionary<string, IReadOnlySet<string>>()).Should().Be("superadmin");
        }

        [Fact]
        public void Grant_StandaloneRole_OnlyWithEveryPermissionItCarries_WhenTheHierarchyIsUsed()
        {
            var lina = User("lina", Hierarchy, true, "lawyer");
            var auditorPermissions = new Dictionary<string, IReadOnlySet<string>>
            {
                ["auditor"] = new HashSet<string> { "reports::export" }
            };

            UserAccessPolicy.FindUngrantableRole(lina, ["auditor"], auditorPermissions).Should().Be("auditor");
        }

        [Fact]
        public void Grant_FlatOrganization_StandaloneRolesStayGrantable()
        {
            var caller = User("lina", Flat, true, "member");
            UserAccessPolicy.FindUngrantableRole(caller, ["member"], new Dictionary<string, IReadOnlySet<string>>
            {
                ["member"] = new HashSet<string> { "anything" }
            }).Should().BeNull();
        }

        [Fact]
        public void Supreme_BypassesTheHierarchy_ButStillCannotGrantAnUnknownRole()
        {
            var supreme = new CallerAccess { Kind = CallerKind.Supreme, OrganizationId = Org, OrganizationRoles = Hierarchy };

            UserAccessPolicy.FindUngrantableRole(supreme, ["admin"], new Dictionary<string, IReadOnlySet<string>>()).Should().BeNull();
            UserAccessPolicy.FindUngrantableRole(supreme, ["ghost"], new Dictionary<string, IReadOnlySet<string>>()).Should().Be("ghost");
        }

        [Fact]
        public void InitialManageList_IsTheAdderAndTheRolesAboveThem_WithoutTheTopRole()
        {
            var list = UserAccessPolicy.InitialManageList("lina", ["lawyer"], Hierarchy, "c1");

            list.Users.Should().BeEquivalentTo(["lina"]);
            list.Roles.Should().BeEquivalentTo(["senior-lawyer"]);
        }

        [Fact]
        public void InitialManageList_NeverListsTheUserOnTheirOwnAccount()
        {
            UserAccessPolicy.InitialManageList("c1", ["lawyer"], Hierarchy, "c1").Users.Should().BeEmpty();
        }
    }
}
