using System.Security.Claims;
using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Utilities;

namespace XUnitTest.Auth.OAuth
{
    /// <summary>
    /// AddClaims no longer decides the organization: it emits the scope handed to it by
    /// <see cref="OrganizationScopeResolver"/>, which is resolved once in
    /// <c>GetJwtAccessToken</c>. The membership rules that used to be inlined here are covered by
    /// <see cref="XUnitTest.IamTests.Shared.OrganizationScopeResolverTests"/>; what these tests
    /// pin down is that the claim mirrors the resolved scope exactly, for all three kinds, and
    /// that every other claim is unaffected.
    /// </summary>
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

        private static OrganizationScope TenantWide() =>
            new(OrganizationScopeKind.TenantWide, IdpConstants.DefaultOrganizationId);

        private static OrganizationScope Organization(string organizationId) =>
            new(OrganizationScopeKind.Organization, organizationId);

        private static OrganizationScope NoOrganization() =>
            new(OrganizationScopeKind.None, IdpConstants.NoOrganizationId);

        [Fact]
        public void AddClaims_AddsCoreClaims_AndTheResolvedTenantWideOrg()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1", TokenVersion = 4, SecurityStamp = "stamp-1" };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(), new TokenRequest(), TenantWide());

            identity.FindFirst(BlocksContext.SUBJECT_CLAIM)!.Value.Should().Be("blocks|u1");
            identity.FindFirst(BlocksContext.USER_ID_CLAIM)!.Value.Should().Be("u1");
            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("default");
            identity.FindFirst("token_version")!.Value.Should().Be("4");
            identity.FindFirst("security_stamp")!.Value.Should().Be("stamp-1");
            identity.FindFirst(BlocksContext.TENANT_ID_CLAIM)!.Value.Should().Be("tenant-1");
            identity.FindAll(BlocksContext.ROLES_CLAIM).Select(c => c.Value).Should().Contain("admin");
            identity.FindAll(BlocksContext.PERMISSION_CLAIM).Select(c => c.Value).Should().Contain("read");
        }

        [Fact] // H2
        public void AddClaims_EmitsTheResolvedOrganization()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1", OrganizationIds = new List<string> { "org-9" } };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(),
                new TokenRequest { OrganizationId = "org-9" }, Organization("org-9"));

            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("org-9");
        }

        [Fact] // H5 -- the state that used to be impossible to express
        public void AddClaims_EmitsNoOrgSentinel_ForTheNoneScope()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1" };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(), new TokenRequest(), NoOrganization());

            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("no-org");
        }

        [Fact] // C1 -- the request no longer influences the claim at this layer at all
        public void AddClaims_IgnoresTokenRequestOrganization_AndTrustsTheResolvedScope()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1", OrganizationIds = new List<string> { "org-1" } };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(),
                new TokenRequest { OrganizationId = "org-not-mine" }, Organization("org-1"));

            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("org-1");
        }

        [Fact] // H6 -- exactly one org_id claim, never duplicated
        public void AddClaims_EmitsExactlyOneOrganizationClaim()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1", OrganizationIds = new List<string> { "org-9" } };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(),
                new TokenRequest { OrganizationId = "org-9" }, Organization("org-9"));

            identity.FindAll(BlocksContext.ORGANIZATION_ID_CLAIM).Should().HaveCount(1);
        }

        [Fact] // C5 -- impersonation still lands on the tenant-wide scope
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
                },
                TenantWide());

            identity.FindFirst(BlocksContext.ORGANIZATION_ID_CLAIM)!.Value.Should().Be("default");
            identity.FindFirst(BlocksContext.IMPERSONATED_CLAIM)!.Value.Should().Be("true");
            identity.FindFirst(BlocksContext.TENANT_ID_CLAIM)!.Value.Should().Be("target-tenant");
        }

        [Fact]
        public void AddClaims_AddsNonce_WhenStatePresent()
        {
            var identity = new ClaimsIdentity("t");
            var user = new User { ItemId = "u1" };

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant(), user, Claims(), new TokenRequest(), TenantWide(),
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

            JwtAccessTokenProvider.AddClaims(identity, BuildTenant("tenant-ignored"), user, Claims(), request, TenantWide());

            identity.FindFirst(BlocksContext.IMPERSONATED_CLAIM)!.Value.Should().Be("true");
            identity.FindFirst(BlocksContext.ORIGINAL_TENANT_ID_CLAIM)!.Value.Should().Be("orig-tenant");
            identity.FindFirst(BlocksContext.TENANT_ID_CLAIM)!.Value.Should().Be("target-tenant");
            identity.FindFirst(BlocksContext.IMPERSONATION_SESSION_ID_CLAIM)!.Value.Should().Be("imp-sess-1");
        }
    }
}
