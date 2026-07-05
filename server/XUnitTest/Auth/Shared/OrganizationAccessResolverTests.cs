using Authentication.DomainService.Shared;
using Authentication.DomainService.Utilities;
using FluentAssertions;
using Iam.DomainService.Entities;

namespace XUnitTest.Auth.Shared
{
    public class OrganizationAccessResolverTests
    {
        [Fact]
        public void ResolveEffectiveOrganizationId_ReturnsLastUsed_WhenStillInOrganizationIds()
        {
            var user = new User
            {
                LastUsedOrganizationId = "org-1",
                OrganizationIds = ["org-1", "org-2"]
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user).Should().Be("org-1");
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_ReturnsDefault_WhenLastUsedNotInOrganizationIds()
        {
            var user = new User
            {
                LastUsedOrganizationId = "org-deleted",
                OrganizationIds = [AuthenticationConstants.DefaultOrganizationId, "org-2"]
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user).Should().Be(AuthenticationConstants.DefaultOrganizationId);
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_FallsBackToFirstOrganizationId()
        {
            var user = new User
            {
                OrganizationIds = ["org-2", "org-3"]
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user).Should().Be("org-2");
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_FallsBackToFirstRoleKey_WhenNoOrgs()
        {
            var user = new User
            {
                Roles = new Dictionary<string, List<string>> { { "role-key", ["admin"] } }
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user).Should().Be("role-key");
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_FallsBackToFirstPermissionKey_WhenNoOrgsOrRoles()
        {
            var user = new User
            {
                Permissions = new Dictionary<string, List<string>> { { "perm-key", ["read"] } }
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user).Should().Be("perm-key");
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_ReturnsNull_WhenAllCollectionsEmpty()
        {
            var user = new User();
            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user).Should().BeNull();
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_IgnoresNullOrEmptyLastUsed()
        {
            var user = new User
            {
                LastUsedOrganizationId = "",
                OrganizationIds = ["org-1"]
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user).Should().Be("org-1");
        }
    }
}