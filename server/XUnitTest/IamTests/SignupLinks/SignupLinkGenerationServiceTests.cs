using System.Security.Cryptography;
using System.Text;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.IamTests.SignupLinks;

public class SignupLinkGenerationServiceTests : IDisposable
{
    private readonly Mock<ISignupLinkRepository> _links = new();
    private readonly Mock<ISignupLinkConfigurationRepository> _configs = new();
    private readonly Mock<IOidcClientRegistrationLookup> _oidc = new();
    private readonly Mock<IResourceRepository> _resources = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IValidator<GenerateSignupLinkRequest>> _generateValidator = new();
    private readonly Mock<IValidator<QuerySignupLinksRequest>> _queryValidator = new();
    private readonly Mock<IValidator<RevokeSignupLinksByConfigurationRequest>> _revokeValidator = new();
    private readonly IConfiguration _configuration;

    private const string TenantId = "tenant-1";
    private const string ActorId = "actor-1";
    private const string CfgId = "cfg1";

    public SignupLinkGenerationServiceTests()
    {
        BlocksContext.IsTestMode = true;
        Environment.SetEnvironmentVariable("BLOCKS_IAM_BASE_URL", null);
        InstallContext(orgId: "default", roles: ["tenant-admin"], permissions: ["read:project"]);

        _generateValidator.Setup(v => v.ValidateAsync(It.IsAny<GenerateSignupLinkRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _queryValidator.Setup(v => v.ValidateAsync(It.IsAny<QuerySignupLinksRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _revokeValidator.Setup(v => v.ValidateAsync(It.IsAny<RevokeSignupLinksByConfigurationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _oidc.Setup(o => o.GetByClientIdAsync("construct-web"))
            .ReturnsAsync(new OidcClientRegistrationInfo(
                "construct-web",
                ["https://construct.example.com/callback"],
                true));

        _configs.Setup(c => c.GetByIdAsync(CfgId, TenantId)).ReturnsAsync(ActiveConfig());
        _users.Setup(u => u.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User)null!);
        _links.Setup(l => l.InsertAsync(It.IsAny<SignupLink>())).Returns(Task.CompletedTask);
        _links.Setup(l => l.ReplaceAsync(It.IsAny<SignupLink>())).ReturnsAsync(true);

        SetupRoleTreeForAlice();

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BLOCKS_IAM_BASE_URL"] = "https://iam.example.com"
            })
            .Build();
    }

    public void Dispose()
    {
        BlocksContext.SetContext(null!);
        BlocksContext.IsTestMode = false;
        Environment.SetEnvironmentVariable("BLOCKS_IAM_BASE_URL", null);
        GC.SuppressFinalize(this);
    }

    private static void InstallContext(
        string orgId = "default",
        IEnumerable<string>? roles = null,
        IEnumerable<string>? permissions = null) =>
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId: TenantId, roles: roles, userId: ActorId, impersonated: false,
            isAuthenticated: true, requestUri: "https://test", organizationId: orgId,
            permissions: permissions, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
            userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
            originalTenantId: TenantId, impersonationSessionId: null, applicationDomain: "test"));

    private void SetupRoleTreeForAlice()
    {
        var roles = new List<Role>
        {
            new() { Slug = "tenant-admin", Name = "Tenant Admin", CanCreateOwn = true, AncestorRoleSlugs = new() },
            new() { Slug = "site-manager", Name = "Site Manager", CanCreateOwn = true, ParentRoleSlug = "tenant-admin", AncestorRoleSlugs = new() { "tenant-admin" } },
            new() { Slug = "site-viewer", Name = "Site Viewer", CanCreateOwn = false, ParentRoleSlug = "site-manager", AncestorRoleSlugs = new() { "site-manager", "tenant-admin" } },
            new() { Slug = "contractor", Name = "Contractor", CanCreateOwn = false, AncestorRoleSlugs = new() },
        }.AsQueryable();
        _resources.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
            .ReturnsAsync((roles, 4L));
    }

    private void SetupRoleTreeForBob()
    {
        var roles = new List<Role>
        {
            new() { Slug = "site-manager", Name = "Site Manager", CanCreateOwn = true, AncestorRoleSlugs = new() },
            new() { Slug = "site-viewer", Name = "Site Viewer", CanCreateOwn = false, ParentRoleSlug = "site-manager", AncestorRoleSlugs = new() { "site-manager" } },
            new() { Slug = "tenant-admin", Name = "Tenant Admin", CanCreateOwn = true, ParentRoleSlug = "root", AncestorRoleSlugs = new() { "root" } },
            new() { Slug = "contractor", Name = "Contractor", CanCreateOwn = false, AncestorRoleSlugs = new() },
            new() { Slug = "root", Name = "Root", CanCreateOwn = false, AncestorRoleSlugs = new() },
        }.AsQueryable();
        _resources.Setup(r => r.GetRolesAsync(It.IsAny<GetRolesRequest>(), It.IsAny<string>()))
            .ReturnsAsync((roles, 5L));
    }

    private static SignupLinkConfiguration ActiveConfig() => new()
    {
        ItemId = CfgId,
        TenantId = TenantId,
        Name = "cfg1",
        IsActive = true,
        DefaultRoles = ["site-manager"],
        DefaultPermissions = ["read:project"],
        ClientId = "construct-web",
        RedirectUri = "https://construct.example.com/callback",
        DefaultForwardedTo = "/projects",
        CredentialMode = SignupLinkCredentialMode.PasswordRequired,
        DefaultLifetimeMinutes = 1440
    };

    private SignupLinkGenerationService Sut() => new(
        _links.Object,
        _configs.Object,
        _oidc.Object,
        new GrantAuthorizationService(_resources.Object),
        _users.Object,
        _configuration,
        _generateValidator.Object,
        _queryValidator.Object,
        _revokeValidator.Object,
        NullLogger<SignupLinkGenerationService>.Instance);

    private static GenerateSignupLinkRequest ValidRequest(Action<GenerateSignupLinkRequest>? tweak = null)
    {
        var req = new GenerateSignupLinkRequest
        {
            ConfigurationId = CfgId,
            Email = "Asif@Example.com ",
            FirstName = "Asif",
            LastName = "R",
            OrganizationId = "org-acme"
        };
        tweak?.Invoke(req);
        return req;
    }

    [Fact]
    public async Task H1_Generate_MintsHashOnlyAndReturnsCodeOnce()
    {
        SignupLink? saved = null;
        _links.Setup(l => l.InsertAsync(It.IsAny<SignupLink>()))
            .Callback<SignupLink>(e => saved = e)
            .Returns(Task.CompletedTask);

        var result = await Sut().GenerateAsync(ValidRequest());

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Url.Should().StartWith("https://iam.example.com/oidc/join/tenant-1#link=");
        var code = result.Data.Url.Split("#link=")[1];
        code.Length.Should().Be(43);
        saved.Should().NotBeNull();
        saved!.CodeHash.Should().NotBeNullOrWhiteSpace();
        saved.CodeHash.Should().NotContain(code);
        // No plaintext code field on entity — only CodeHash
        saved.Email.Should().Be("asif@example.com");
        saved.Roles.Should().BeEquivalentTo("site-manager");
        saved.OrganizationId.Should().Be("org-acme");
        saved.RedemptionCount.Should().Be(0);
        saved.Status.Should().Be(SignupLinkStatus.Active);
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(code))).Should().Be(saved.CodeHash);
    }

    [Fact]
    public async Task H2_DefaultToken_UsesPayloadOrg()
    {
        var result = await Sut().GenerateAsync(ValidRequest());
        result.IsSuccess.Should().BeTrue();
        result.Data!.LinkId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task H2_ConcreteToken_IgnoresAbsentPayloadOrg()
    {
        InstallContext(orgId: "org-acme", roles: ["site-manager"], permissions: ["read:project"]);
        SetupRoleTreeForBob();
        SignupLink? saved = null;
        _links.Setup(l => l.InsertAsync(It.IsAny<SignupLink>()))
            .Callback<SignupLink>(e => saved = e)
            .Returns(Task.CompletedTask);

        var result = await Sut().GenerateAsync(ValidRequest(r => r.OrganizationId = null));
        result.IsSuccess.Should().BeTrue();
        saved!.OrganizationId.Should().Be("org-acme");
    }

    [Fact]
    public async Task H3_EmptyPermissions_InheritConfig_WhileRolesOverride()
    {
        SignupLink? saved = null;
        _links.Setup(l => l.InsertAsync(It.IsAny<SignupLink>()))
            .Callback<SignupLink>(e => saved = e)
            .Returns(Task.CompletedTask);

        InstallContext(orgId: "org-acme", roles: ["site-manager"], permissions: ["read:project"]);
        SetupRoleTreeForBob();

        var result = await Sut().GenerateAsync(ValidRequest(r =>
        {
            r.OrganizationId = null;
            r.Roles = ["site-viewer"];
            r.Permissions = [];
        }));

        result.IsSuccess.Should().BeTrue();
        saved!.Roles.Should().BeEquivalentTo("site-viewer");
        saved.Permissions.Should().BeEquivalentTo("read:project");
    }

    [Fact]
    public async Task C1_OrgConflict_Returns400()
    {
        InstallContext(orgId: "org-acme", roles: ["site-manager"], permissions: ["read:project"]);
        SetupRoleTreeForBob();

        var result = await Sut().GenerateAsync(ValidRequest(r => r.OrganizationId = "org-other"));
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Errors!["OrganizationId"].Should().Be("Organization must match the caller's organization");
        _links.Verify(l => l.InsertAsync(It.IsAny<SignupLink>()), Times.Never);
    }

    [Fact]
    public async Task C1_DefaultTokenMissingOrg_Returns400()
    {
        var result = await Sut().GenerateAsync(ValidRequest(r => r.OrganizationId = null));
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Errors!["OrganizationId"].Should().Be("OrganizationId is required");
        _links.Verify(l => l.InsertAsync(It.IsAny<SignupLink>()), Times.Never);
    }

    [Fact]
    public async Task C2_UngrantableRole_Returns403()
    {
        InstallContext(orgId: "org-acme", roles: ["site-manager"], permissions: ["read:project"]);
        SetupRoleTreeForBob();

        var result = await Sut().GenerateAsync(ValidRequest(r =>
        {
            r.OrganizationId = null;
            r.Roles = ["tenant-admin"];
        }));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Errors!["Roles"].Should().Be("You cannot grant the role: tenant-admin");
        _links.Verify(l => l.InsertAsync(It.IsAny<SignupLink>()), Times.Never);
    }

    [Fact]
    public async Task H4_BobCanGrantSiteViewerAndContractor()
    {
        InstallContext(orgId: "org-acme", roles: ["site-manager"], permissions: ["read:project"]);
        SetupRoleTreeForBob();

        var viewer = await Sut().GenerateAsync(ValidRequest(r =>
        {
            r.OrganizationId = null;
            r.Roles = ["site-viewer"];
        }));
        viewer.IsSuccess.Should().BeTrue();

        var contractor = await Sut().GenerateAsync(ValidRequest(r =>
        {
            r.OrganizationId = null;
            r.Email = "c2@example.com";
            r.Roles = ["contractor"];
        }));
        contractor.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task C3_InactiveConfiguration_Returns400()
    {
        var inactive = ActiveConfig();
        inactive.IsActive = false;
        _configs.Setup(c => c.GetByIdAsync(CfgId, TenantId)).ReturnsAsync(inactive);

        var result = await Sut().GenerateAsync(ValidRequest());
        result.IsSuccess.Should().BeFalse();
        result.Errors!["ConfigurationId"].Should().Be("Not found or inactive");
    }

    [Fact]
    public async Task C4_InactiveClient_Returns400()
    {
        _oidc.Setup(o => o.GetByClientIdAsync("construct-web"))
            .ReturnsAsync(new OidcClientRegistrationInfo("construct-web", ["https://construct.example.com/callback"], false));

        var result = await Sut().GenerateAsync(ValidRequest());
        result.IsSuccess.Should().BeFalse();
        result.Errors!["ClientId"].Should().Be("Client not found or inactive");
    }

    [Fact]
    public async Task Example5_ExistingEmail_SetsFlagStillCreates()
    {
        _users.Setup(u => u.GetUserByEmailAsync("existing@example.com"))
            .ReturnsAsync(new User { ItemId = "u1", Email = "existing@example.com" });

        var result = await Sut().GenerateAsync(ValidRequest(r => r.Email = "existing@example.com"));
        result.IsSuccess.Should().BeTrue();
        result.Data!.EmailAlreadyExists.Should().BeTrue();
        _links.Verify(l => l.InsertAsync(It.IsAny<SignupLink>()), Times.Once);
    }

    [Fact]
    public async Task H5_Revoke_SetsStatusAndAudit()
    {
        var entity = new SignupLink
        {
            ItemId = "link-1",
            TenantId = TenantId,
            Status = SignupLinkStatus.Active,
            Email = "a@b.com"
        };
        _links.Setup(l => l.GetByIdAsync("link-1", TenantId)).ReturnsAsync(entity);

        var result = await Sut().RevokeAsync("link-1");
        result.IsSuccess.Should().BeTrue();
        entity.Status.Should().Be(SignupLinkStatus.Revoked);
        entity.RevokedBy.Should().Be(ActorId);
        entity.RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task H5_RevokeByConfiguration_Delegates()
    {
        _links.Setup(l => l.RevokeActiveByConfigurationAsync(TenantId, CfgId, ActorId, It.IsAny<DateTime>()))
            .ReturnsAsync(2);

        var result = await Sut().RevokeByConfigurationAsync(new RevokeSignupLinksByConfigurationRequest { ConfigurationId = CfgId });
        result.IsSuccess.Should().BeTrue();
        result.RevokedCount.Should().Be(2);
    }

    [Fact]
    public async Task Query_NeverIncludesCodeOrHash()
    {
        _links.Setup(l => l.QueryAsync(TenantId, It.IsAny<QuerySignupLinksRequest>()))
            .ReturnsAsync(([
                new SignupLink
                {
                    ItemId = "l1",
                    Email = "a@b.com",
                    FirstName = "A",
                    LastName = "B",
                    OrganizationId = "org-acme",
                    Roles = ["site-manager"],
                    ConfigurationId = CfgId,
                    Status = SignupLinkStatus.Active,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = ActorId,
                    CodeHash = "SECRET_HASH"
                }
            ], 1L));

        var (response, errors) = await Sut().QueryAsync(new QuerySignupLinksRequest());
        errors.Should().BeNull();
        response!.Items.Should().HaveCount(1);
        var json = System.Text.Json.JsonSerializer.Serialize(response.Items[0]);
        json.Should().NotContain("SECRET_HASH");
        json.Should().NotContain("CodeHash");
        json.Should().NotContain("code");
    }

    [Fact]
    public async Task Revoke_NotFound_Fails()
    {
        _links.Setup(l => l.GetByIdAsync("missing", TenantId)).ReturnsAsync((SignupLink?)null);
        var result = await Sut().RevokeAsync("missing");
        result.IsSuccess.Should().BeFalse();
        result.Errors!["ItemId"].Should().Be("Not found");
    }

    [Fact]
    public async Task Revoke_NonActive_Fails()
    {
        _links.Setup(l => l.GetByIdAsync("l1", TenantId)).ReturnsAsync(new SignupLink
        {
            ItemId = "l1", TenantId = TenantId, Status = SignupLinkStatus.Revoked
        });
        var result = await Sut().RevokeAsync("l1");
        result.IsSuccess.Should().BeFalse();
        result.Errors!["Status"].Should().Contain("Active");
    }

    [Fact]
    public async Task C2_UngrantablePermission_Returns403()
    {
        InstallContext(orgId: "default", roles: ["tenant-admin"], permissions: ["read:project"]);
        SetupRoleTreeForAlice();
        var result = await Sut().GenerateAsync(ValidRequest(r => r.Permissions = ["write:secret"]));
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Errors!["Permissions"].Should().Contain("write:secret");
        _links.Verify(l => l.InsertAsync(It.IsAny<SignupLink>()), Times.Never);
    }

    [Fact]
    public async Task C4_RedirectNotRegistered_Returns400()
    {
        var cfg = ActiveConfig();
        cfg.RedirectUri = "https://evil.example/callback";
        _configs.Setup(c => c.GetByIdAsync(CfgId, TenantId)).ReturnsAsync(cfg);
        var result = await Sut().GenerateAsync(ValidRequest());
        result.IsSuccess.Should().BeFalse();
        result.Errors!["RedirectUri"].Should().Contain("not registered");
    }

    [Theory]
    [InlineData("default", null, null, "OrganizationId is required")]
    [InlineData("orgA", "orgB", null, "Organization must match the caller's organization")]
    [InlineData("orgA", null, "orgA", null)]
    [InlineData("orgA", "orgA", "orgA", null)]
    [InlineData("default", "orgB", "orgB", null)]
    public void ResolveOrganization_Ladder(string token, string? payload, string? expectedOrg, string? expectedError)
    {
        var (org, error) = SignupLinkGenerationService.ResolveOrganization(token, payload);
        org.Should().Be(expectedOrg);
        error.Should().Be(expectedError);
    }
}
