using Blocks.Genesis.Entities.Data;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.AccessGrant;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestDoubles;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.AccessGrant
{
    /// <summary>
    /// The reads behind access grant: the roles a data-access policy names (R11) and the
    /// holders of a role (R12).
    /// </summary>
    public class AccessGrantRepositoryTests
    {
        private readonly Mock<IResourceRepository> _resources = new();
        private readonly Mock<IUserRepository> _users = new();
        private readonly Mock<IIdentityAccessManagementRepository> _iam = new();

        private AccessGrantRepository Sut() => new(_resources.Object, _users.Object, _iam.Object, new PassThroughIamDataAccess());

        private static PolicyRule AuthRoles(object value) => new()
        {
            LeftSource = ConditionSource.AUTH,
            LeftOperand = "Roles",
            RightSource = ConditionSource.STATIC_VALUE,
            StaticValue = value
        };

        [Fact]
        public async Task PolicyRolesAreReadFromAuthRolesRulesAtAnyDepthEitherWayRound()
        {
            var groups = new List<PolicyRuleGroup>
            {
                new()
                {
                    Rules = [AuthRoles("hr-admin"), new PolicyRule { LeftSource = ConditionSource.AUTH, LeftOperand = "Email", StaticValue = "x" }],
                    NestedGroups = [new PolicyRuleGroup { Rules = [AuthRoles(new object[] { "auditor", "finance" })] }]
                },
                new()
                {
                    Rules =
                    [
                        new PolicyRule
                        {
                            LeftSource = ConditionSource.STATIC_VALUE,
                            StaticValue = "silver",
                            RightSource = ConditionSource.AUTH,
                            RightOperand = "roles"
                        }
                    ]
                }
            };
            var collection = MongoMock.Collection<DataAccessPolicy>();
            MongoMock.SetupProjectedFind(collection, groups);
            _iam.Setup(i => i.GetCollectionByName<DataAccessPolicy>("DataAccessPolicys")).Returns(collection.Object);

            var slugs = await Sut().GetPolicyRoleSlugsAsync();

            slugs.Should().BeEquivalentTo("hr-admin", "auditor", "finance", "silver");
        }

        [Fact]
        public async Task RoleHoldersAreProjectedToTheirIds()
        {
            var collection = MongoMock.Collection<User>();
            MongoMock.SetupProjectedFind(collection, new List<string> { "u1", "u2" });
            _iam.Setup(i => i.GetCollection<User>()).Returns(collection.Object);

            var ids = await Sut().GetUserIdsWithRoleAsync("viewer", "org-1");

            ids.Should().Equal("u1", "u2");
            (await Sut().GetUserIdsWithRoleAsync("", "org-1")).Should().BeEmpty();
        }

        [Fact]
        public async Task RolesAndPermissionsComeFromTheOrganizationReads()
        {
            _resources.Setup(r => r.GetRolesByOrgAsync("org-1")).ReturnsAsync([new Role { Slug = "a" }]);
            var permissions = MongoMock.Collection(new[] { new Permission { Resource = "x", OrganizationId = "org-1" } });
            _iam.Setup(i => i.GetCollection<Permission>()).Returns(permissions.Object);
            _users.Setup(u => u.GetUserByIdAsync("u1")).ReturnsAsync(new User { ItemId = "u1" });

            (await Sut().GetRolesAsync("org-1")).Should().ContainSingle();
            (await Sut().GetPermissionsAsync("org-1")).Should().ContainSingle();
            (await Sut().GetUserAsync("u1"))!.ItemId.Should().Be("u1");
            (await Sut().GetUserAsync(" ")).Should().BeNull();
        }
    }
}
