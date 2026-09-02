using Authentication.DomainService.Shared;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Utilities;
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

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user, isMultiOrgEnabled: true).Should().Be("org-1");
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_ReturnsDefault_WhenLastUsedNotInOrganizationIds()
        {
            var user = new User
            {
                LastUsedOrganizationId = "org-deleted",
                OrganizationIds = [IdpConstants.DefaultOrganizationId, "org-2"]
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user, isMultiOrgEnabled: true).Should().Be(IdpConstants.DefaultOrganizationId);
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_FallsBackToFirstOrganizationId()
        {
            var user = new User
            {
                OrganizationIds = ["org-2", "org-3"]
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user, isMultiOrgEnabled: true).Should().Be("org-2");
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_FallsBackToFirstRoleKey_WhenNoOrgs()
        {
            var user = new User
            {
                Roles = new Dictionary<string, List<string>> { { "role-key", ["admin"] } }
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user, isMultiOrgEnabled: true).Should().Be("role-key");
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_FallsBackToFirstPermissionKey_WhenNoOrgsOrRoles()
        {
            var user = new User
            {
                Permissions = new Dictionary<string, List<string>> { { "perm-key", ["read"] } }
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user, isMultiOrgEnabled: true).Should().Be("perm-key");
        }

        [Fact] // H5 -- this used to return null, which made the organization claim vanish entirely
        public void ResolveEffectiveOrganizationId_ReturnsNoOrgSentinel_WhenAllCollectionsEmpty()
        {
            var user = new User();
            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user, isMultiOrgEnabled: true)
                .Should().Be(IdpConstants.NoOrganizationId);
        }

        [Fact] // H1 -- a single-organization project always mints the tenant-wide scope
        public void ResolveEffectiveOrganizationId_ReturnsDefault_WhenMultiOrgDisabled()
        {
            var user = new User { OrganizationIds = ["org-1"] };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user, isMultiOrgEnabled: false)
                .Should().Be(IdpConstants.DefaultOrganizationId);
        }

        [Fact] // C2 -- a sentinel supplied by the caller is discarded, not honoured
        public void ResolveSignInOrganizationId_DiscardsTheNoOrgSentinel()
        {
            var user = new User { OrganizationIds = ["org-1"] };

            OrganizationAccessResolver.ResolveSignInOrganizationId(user, IdpConstants.NoOrganizationId)
                .Should().Be("org-1");
        }

        [Fact] // H5 on the sign-in legs too -- never "default" by omission
        public void ResolveSignInOrganizationId_ReturnsNoOrgSentinel_WhenTheUserBelongsToNothing()
        {
            OrganizationAccessResolver.ResolveSignInOrganizationId(new User(), requestedOrganizationId: null)
                .Should().Be(IdpConstants.NoOrganizationId);
        }

        [Fact]
        public void ResolveEffectiveOrganizationId_IgnoresNullOrEmptyLastUsed()
        {
            var user = new User
            {
                LastUsedOrganizationId = "",
                OrganizationIds = ["org-1"]
            };

            OrganizationAccessResolver.ResolveEffectiveOrganizationId(user, isMultiOrgEnabled: true).Should().Be("org-1");
        }
    }
}