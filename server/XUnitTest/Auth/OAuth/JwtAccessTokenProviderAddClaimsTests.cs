using System.Security.Claims;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;

namespace XUnitTest.Auth.OAuth
{
    public class JwtAccessTokenProviderAddClaimsTests
    {
        private static Tenant BuildTenant(string tenantId = "tenant-1") => new()
        {
            TenantId = tenantId,
            DbConnectionString = string.Empty,
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = string.Empty, IssueDate = DateTime.UtcNow },
            Applications = new List<Applications>()
        };

        private static ResolvedAuthorizationClaims Claims(List<string>? roles = null, List<string>? perms = null) => new()
        {
            Roles = roles ?? new List<string> { "admin" },
            Permissions = perms ?? new List<string> { "read" }
        };

        [Fact]
        public void AddClaims_AddsCoreClaims_AndDefaultOrg()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1", TokenVersion = 4, SecurityStamp = "stamp-1" };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(), new TokenRequest());

            identity.FindFirst(BlocksContext.SUBJECT_CLAIM)!.Value.Should().Be("blocks|u1");
            identity.FindFirst(BlocksContext.USER_ID_CLAIM)!.Value.Should().Be("u1");
            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("default");
            identity.FindFirst("token_version")!.Value.Should().Be("4");
            identity.FindFirst("security_stamp")!.Value.Should().Be("stamp-1");
            identity.FindFirst(BlocksContext.TENANT_ID_CLAIM)!.Value.Should().Be("tenant-1");
            identity.FindAll(BlocksContext.ROLES_CLAIM).Select(c => c.Value).Should().Contain("admin");
            identity.FindAll(BlocksContext.PERMISSION_CLAIM).Select(c => c.Value).Should().Contain("read");
        }

        [Fact]
        public void AddClaims_UsesRequestedOrg_WhenUserBelongs()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1", OrganizationIds = new List<string> { "org-9" } };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(),
                new TokenRequest { OrganizationId = "org-9" });

            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("org-9");
        }

        [Fact]
        public void AddClaims_FallsBackToDefaultOrg_WhenUserNotInRequestedOrg()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1", OrganizationIds = new List<string> { "org-1" } };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(),
                new TokenRequest { OrganizationId = "org-not-mine" });

            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("default");
        }

        [Fact] // C4 -- switch-org authorises via Roles, so the claim must carry that organization
        public void AddClaims_UsesRequestedOrg_WhenGrantedThroughRolesOnly()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User
            {
                ItemId = "u1",
                OrganizationIds = new List<string> { "default" },
                Roles = new Dictionary<string, List<string>> { ["org-a"] = new() { "admin" } }
            };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(),
                new TokenRequest { OrganizationId = "org-a" });

            // Before this guard was widened the claim silently fell back to "default", which put the
            // user in the tenant-wide user-list scope instead of the organization they switched into.
            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("org-a");
        }

        [Fact] // C4 -- same, granted through Permissions
        public void AddClaims_UsesRequestedOrg_WhenGrantedThroughPermissionsOnly()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User
            {
                ItemId = "u1",
                OrganizationIds = new List<string> { "default" },
                Permissions = new Dictionary<string, List<string>> { ["org-a"] = new() { "read" } }
            };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(),
                new TokenRequest { OrganizationId = "org-a" });

            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("org-a");
        }

        [Fact] // C5 -- impersonation: the cloud user matches none of the three, so it stays "default"
        public void AddClaims_Impersonation_StillResolvesToDefaultOrg()
        {
            var identity = new ClaimsIdentity("t");
            var cloudUser = new User { ItemId = "cloud-1", OrganizationIds = new List<string> { "default" } };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), cloudUser, Claims(),
                new TokenRequest
                {
                    OrganizationId = "target-tenant-org",
                    IsImpersonation = true,
                    OriginalTenantId = "root-tenant",
                    TargetTenantId = "target-tenant"
                });

            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("default");
            identity.FindFirst(BlocksContext.IMPERSONATED_CLAIM)!.Value.Should().Be("true");
            identity.FindFirst(BlocksContext.TENANT_ID_CLAIM)!.Value.Should().Be("target-tenant");
        }

        [Fact]
        public void AddClaims_AddsNonce_WhenStatePresent()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1" };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(), new TokenRequest(),
                stateInfo: new StateInfo { Nonce = "nonce-xyz", Audience = "aud", ClientId = "c1", Provider = "blocks" });

            identity.FindFirst("nonce")!.Value.Should().Be("nonce-xyz");
        }

        [Fact]
        public void AddClaims_Impersonation_AddsImpersonationClaims_AndTargetTenant()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1" };
            var request = new TokenRequest
            {
                IsImpersonation = true,
                OriginalTenantId = "orig-tenant",
                TargetTenantId = "target-tenant",
                ImpersonationSessionId = "imp-sess-1"
            };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant("tenant-ignored"), user, Claims(), request);

            identity.FindFirst(BlocksContext.IMPERSONATED_CLAIM)!.Value.Should().Be("true");
            identity.FindFirst(BlocksContext.ORIGINAL_TENANT_ID_CLAIM)!.Value.Should().Be("orig-tenant");
            identity.FindFirst(BlocksContext.TENANT_ID_CLAIM)!.Value.Should().Be("target-tenant");
            identity.FindFirst(BlocksContext.IMPERSONATION_SESSION_ID_CLAIM)!.Value.Should().Be("imp-sess-1");
        }
    }
}
