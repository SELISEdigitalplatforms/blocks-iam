using Blocks.Genesis;
using Blocks.Genesis.DataAccess;
using Blocks.Genesis.DataAccess.Evaluation;
using Blocks.Genesis.Entities.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Authentication.DomainService.Utilities;
using Iam.DomainService.Services;
using Xunit;

namespace XUnitTest.Auth.Shared;

/// <summary>An impersonated token bypasses data access policies, as in the Data Gateway; every other caller is held to them.</summary>
public class BlocksContextPrincipalAccessorTests
{
    private static BlocksContext Context(bool impersonated) => BlocksContext.Create(
        tenantId: "tenant-1",
        roles: ["user"],
        userId: "user-1",
        isAuthenticated: true,
        requestUri: "http://test",
        organizationId: "org-1",
        expireOn: DateTime.UtcNow.AddHours(1),
        email: "user@x.com",
        permissions: [],
        userName: "user@x.com",
        phoneNumber: string.Empty,
        displayName: string.Empty,
        oauthToken: string.Empty,
        originalTenantId: impersonated ? "root-tenant" : "tenant-1",
        applicationDomain: string.Empty,
        impersonated: impersonated,
        impersonationSessionId: impersonated ? "session-1" : string.Empty);

    private static PolicyEvaluationResult CheckCustomRead(AccessPrincipal principal) =>
        new DataAccessPolicyResolutionService(new SchemaPolicySource()).Check(new SchemaAccessRequest(
            principal,
            new SchemaDefinitionExtended { SchemaName = "Thing", ReadAccessLevel = SchemaAccessLevel.Custom, Policies = [] },
            PolicyOperation.READ));

    [Fact]
    public void ImpersonatedCaller_BypassesCustomSchemaPolicies()
    {
        var principal = BlocksContextPrincipalAccessor.For(Context(impersonated: true));

        principal.IsRootTenant.Should().BeTrue();
        CheckCustomRead(principal).IsAccessGranted.Should().BeTrue();
    }

    [Fact]
    public void NormalCaller_IsHeldToCustomSchemaPolicies()
    {
        var principal = BlocksContextPrincipalAccessor.For(Context(impersonated: false));

        principal.IsRootTenant.Should().BeFalse();
        CheckCustomRead(principal).IsAccessGranted.Should().BeFalse();
    }

    [Fact]
    public void NoContext_IsNotTreatedAsImpersonated() =>
        BlocksContextPrincipalAccessor.For(null).IsRootTenant.Should().BeFalse();

    [Fact]
    public void Registration_UsesTheImpersonationAwareAccessor()
    {
        var services = new ServiceCollection();
        services.RegisterAllServices();

        services.Last(d => d.ServiceType == typeof(IAccessPrincipalAccessor)).ImplementationType
            .Should().Be(typeof(BlocksContextPrincipalAccessor));
    }
}
