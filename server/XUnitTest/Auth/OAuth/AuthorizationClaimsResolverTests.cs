using Authentication.DomainService.OAuth;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Moq;

namespace XUnitTest.Auth.OAuth
{
    public class AuthorizationClaimsResolverTests
    {
        private static AuthorizationClaimsResolver CreateResolver()
        {
            return new AuthorizationClaimsResolver(Mock.Of<IUserRepository>());
        }

        [Fact]
        public async Task ResolveAsync_ReturnsEmptyLists_WhenUserHasNoRolesOrPermissions()
        {
            var resolver = CreateResolver();

            var claims = await resolver.ResolveAsync(new User(), null);

            claims.Roles.Should().BeEmpty();
            claims.Permissions.Should().BeEmpty();
        }

        [Fact]
        public async Task ResolveAsync_ReturnsRoles_ForOrganizationId()
        {
            var user = new User
            {
                Roles = new Dictionary<string, List<string>>
                {
                    { "org-1", ["admin", "editor"] }
                }
            };

            var resolver = CreateResolver();
            var claims = await resolver.ResolveAsync(user, "org-1");

            claims.Roles.Should().BeEquivalentTo(["admin", "editor"]);
        }

        [Fact]
        public async Task ResolveAsync_ReturnsPermissions_ForOrganizationId()
        {
            var user = new User
            {
                Permissions = new Dictionary<string, List<string>>
                {
                    { "org-1", ["read", "write"] }
                }
            };

            var resolver = CreateResolver();
            var claims = await resolver.ResolveAsync(user, "org-1");

            claims.Permissions.Should().BeEquivalentTo(["read", "write"]);
        }

        [Fact]
        public async Task ResolveAsync_FallsBackToDefault_WhenOrganizationIdNull()
        {
            var user = new User
            {
                Roles = new Dictionary<string, List<string>>
                {
                    { "default", ["user"] }
                }
            };

            var resolver = CreateResolver();
            var claims = await resolver.ResolveAsync(user, null);

            claims.Roles.Should().Contain("user");
        }

        [Fact]
        public async Task ResolveAsync_ReturnsEmpty_WhenOrganizationIdNotInRoles()
        {
            var user = new User
            {
                Roles = new Dictionary<string, List<string>>
                {
                    { "other-org", ["admin"] }
                }
            };

            var resolver = CreateResolver();
            var claims = await resolver.ResolveAsync(user, "missing-org");

            claims.Roles.Should().BeEmpty();
        }

        [Fact]
        public async Task ResolveAsync_DeduplicatesRoles_CaseInsensitively()
        {
            var user = new User
            {
                Roles = new Dictionary<string, List<string>>
                {
                    { "org-1", ["Admin", "ADMIN", "user"] }
                }
            };

            var resolver = CreateResolver();
            var claims = await resolver.ResolveAsync(user, "org-1");

            claims.Roles.Should().HaveCount(2);
        }

        [Fact]
        public async Task ResolveAsync_DeduplicatesPermissions_CaseInsensitively()
        {
            var user = new User
            {
                Permissions = new Dictionary<string, List<string>>
                {
                    { "org-1", ["Read", "READ"] }
                }
            };

            var resolver = CreateResolver();
            var claims = await resolver.ResolveAsync(user, "org-1");

            claims.Permissions.Should().HaveCount(1);
        }

        [Fact]
        public async Task ResolveAsync_FiltersOutEmptyAndWhitespace()
        {
            var user = new User
            {
                Roles = new Dictionary<string, List<string>>
                {
                    { "org-1", ["admin", "", "  ", "user"] }
                }
            };

            var resolver = CreateResolver();
            var claims = await resolver.ResolveAsync(user, "org-1");

            claims.Roles.Should().HaveCount(2);
            claims.Roles.Should().Contain(["admin", "user"]);
        }
    }
}