using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Moq;

namespace XUnitTest.IamTests.SignupLinks;

public class SignupLinkConfigurationServiceTests : IDisposable
{
    private readonly Mock<ISignupLinkConfigurationRepository> _repo = new();
    private readonly Mock<IOidcClientRegistrationLookup> _oidc = new();
    private readonly Mock<IResourceRepository> _resources = new();
    private readonly Mock<IValidator<CreateSignupLinkConfigurationRequest>> _createValidator = new();
    private readonly Mock<IValidator<UpdateSignupLinkConfigurationRequest>> _updateValidator = new();
    private readonly Mock<IValidator<QuerySignupLinkConfigurationsRequest>> _queryValidator = new();

    private const string TenantId = "tenant-1";
    private const string ActorId = "actor-1";

    public SignupLinkConfigurationServiceTests()
    {
        BlocksContext.IsTestMode = true;
        InstallContext();
        _createValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateSignupLinkConfigurationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _updateValidator.Setup(v => v.ValidateAsync(It.IsAny<UpdateSignupLinkConfigurationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _queryValidator.Setup(v => v.ValidateAsync(It.IsAny<QuerySignupLinkConfigurationsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _oidc.Setup(o => o.GetByClientIdAsync("construct-web"))
            .ReturnsAsync(new OidcClientRegistrationInfo(
                "construct-web",
                ["https://construct.example.com/callback"],
                true));
        _resources.Setup(r => r.GetNonArchivedRolesBySlugAsync("site-manager"))
            .ReturnsAsync([new Role { Slug = "site-manager", Name = "Site Manager" }]);
        _repo.Setup(r => r.FindByNameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((SignupLinkConfiguration?)null);
        _repo.Setup(r => r.InsertAsync(It.IsAny<SignupLinkConfiguration>())).Returns(Task.CompletedTask);
        _repo.Setup(r => r.ReplaceAsync(It.IsAny<SignupLinkConfiguration>())).ReturnsAsync(true);
    }

    public void Dispose()
    {
        BlocksContext.SetContext(null!);
        BlocksContext.IsTestMode = false;
        GC.SuppressFinalize(this);
    }

    private static void InstallContext(string tenantId = TenantId) =>
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId: tenantId, roles: null, userId: ActorId, impersonated: false,
            isAuthenticated: true, requestUri: "https://test", organizationId: "default",
            permissions: null, expireOn: DateTime.UtcNow.AddHours(1), email: "a@b.com",
            userName: "tester", phoneNumber: null, displayName: "T", oauthToken: null,
            originalTenantId: tenantId, impersonationSessionId: null, applicationDomain: "test"));

    private SignupLinkConfigurationService Sut() => new(
        _repo.Object, _oidc.Object, _resources.Object,
        _createValidator.Object, _updateValidator.Object, _queryValidator.Object);

    private static CreateSignupLinkConfigurationRequest ValidCreate(Action<CreateSignupLinkConfigurationRequest>? tweak = null)
    {
        var req = new CreateSignupLinkConfigurationRequest
        {
            Name = "Construct Site Manager",
            DefaultRoles = ["site-manager"],
            DefaultPermissions = [],
            ClientId = "construct-web",
            RedirectUri = "https://construct.example.com/callback",
            DefaultForwardedTo = "/projects",
            CredentialMode = SignupLinkCredentialMode.PasswordRequired,
            DefaultLifetimeMinutes = 1440
        };
        tweak?.Invoke(req);
        return req;
    }

    [Fact]
    public async Task H1_Create_PersistsActiveAndReturnsItemId()
    {
        SignupLinkConfiguration? saved = null;
        _repo.Setup(r => r.InsertAsync(It.IsAny<SignupLinkConfiguration>()))
            .Callback<SignupLinkConfiguration>(e => saved = e)
            .Returns(Task.CompletedTask);

        var result = await Sut().CreateAsync(ValidCreate());

        result.IsSuccess.Should().BeTrue();
        result.ItemId.Should().NotBeNullOrWhiteSpace();
        saved.Should().NotBeNull();
        saved!.IsActive.Should().BeTrue();
        saved.TenantId.Should().Be(TenantId);
        saved.Name.Should().Be("Construct Site Manager");
        saved.CredentialMode.Should().Be(SignupLinkCredentialMode.PasswordRequired);
    }

    [Fact]
    public async Task Create_SignInAfterActivation_PersistsOnPasswordRequired()
    {
        SignupLinkConfiguration? saved = null;
        _repo.Setup(r => r.InsertAsync(It.IsAny<SignupLinkConfiguration>()))
            .Callback<SignupLinkConfiguration>(e => saved = e)
            .Returns(Task.CompletedTask);

        var result = await Sut().CreateAsync(ValidCreate(r => r.SignInAfterActivation = true));

        result.IsSuccess.Should().BeTrue();
        saved!.SignInAfterActivation.Should().BeTrue();
    }

    [Fact]
    public async Task Create_SignInAfterActivation_IsRejectedOnPasswordless()
    {
        // Passwordless never mints an activation key, so there is no activation for the flag
        // to act on. Refused rather than stored and silently ignored.
        var result = await Sut().CreateAsync(ValidCreate(r =>
        {
            r.CredentialMode = SignupLinkCredentialMode.Passwordless;
            r.SignInAfterActivation = true;
        }));

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainKey("SignInAfterActivation");
    }

    [Fact]
    public async Task Create_DefaultsSignInAfterActivationToFalse()
    {
        SignupLinkConfiguration? saved = null;
        _repo.Setup(r => r.InsertAsync(It.IsAny<SignupLinkConfiguration>()))
            .Callback<SignupLinkConfiguration>(e => saved = e)
            .Returns(Task.CompletedTask);

        await Sut().CreateAsync(ValidCreate());

        // Day one is a no-op everywhere: an omitted flag means today's behaviour.
        saved!.SignInAfterActivation.Should().BeFalse();
    }

    [Fact]
    public async Task H2_Create_AppliesDefaultTtlAndNullMaxRedemptions()
    {
        SignupLinkConfiguration? saved = null;
        _repo.Setup(r => r.InsertAsync(It.IsAny<SignupLinkConfiguration>()))
            .Callback<SignupLinkConfiguration>(e => saved = e)
            .Returns(Task.CompletedTask);

        var result = await Sut().CreateAsync(ValidCreate(r =>
        {
            r.DefaultLifetimeMinutes = null;
            r.DefaultMaxRedemptions = null;
        }));

        result.IsSuccess.Should().BeTrue();
        saved!.DefaultLifetimeMinutes.Should().Be(1440);
        saved.DefaultMaxRedemptions.Should().BeNull();
    }

    [Fact]
    public async Task H3_Patch_OnlySuppliedFieldsAndTouchesLastUpdated()
    {
        var existing = Stored();
        var originalRoles = existing.DefaultRoles.ToList();
        var originalUpdated = existing.LastUpdatedDate;
        _repo.Setup(r => r.GetByIdAsync(existing.ItemId, TenantId)).ReturnsAsync(existing);

        var result = await Sut().UpdateAsync(existing.ItemId, new UpdateSignupLinkConfigurationRequest
        {
            Description = "patched only"
        });

        result.IsSuccess.Should().BeTrue();
        existing.Description.Should().Be("patched only");
        existing.DefaultRoles.Should().Equal(originalRoles);
        existing.ClientId.Should().Be("construct-web");
        existing.LastUpdatedDate.Should().BeAfter(originalUpdated);
        existing.LastUpdatedBy.Should().Be(ActorId);
        _repo.Verify(r => r.ReplaceAsync(existing), Times.Once);
    }

    [Fact]
    public async Task H4_Archive_SoftDeletesAndQueryExcludesUnlessIncludeInactive()
    {
        var existing = Stored();
        _repo.Setup(r => r.GetByIdAsync(existing.ItemId, TenantId)).ReturnsAsync(existing);

        var archive = await Sut().ArchiveAsync(existing.ItemId);
        archive.IsSuccess.Should().BeTrue();
        existing.IsActive.Should().BeFalse();

        _repo.Setup(r => r.QueryAsync(TenantId, It.Is<QuerySignupLinkConfigurationsRequest>(q => !q.IncludeInactive)))
            .ReturnsAsync((new List<SignupLinkConfiguration>(), 0L));
        _repo.Setup(r => r.QueryAsync(TenantId, It.Is<QuerySignupLinkConfigurationsRequest>(q => q.IncludeInactive)))
            .ReturnsAsync((new List<SignupLinkConfiguration> { existing }, 1L));

        var (activeOnly, _) = await Sut().QueryAsync(new QuerySignupLinkConfigurationsRequest { Page = 0, PageSize = 20 });
        activeOnly!.TotalCount.Should().Be(0);

        var (withInactive, _) = await Sut().QueryAsync(new QuerySignupLinkConfigurationsRequest
        {
            Page = 0, PageSize = 20, IncludeInactive = true
        });
        withInactive!.TotalCount.Should().Be(1);
        withInactive.Items[0].IsActive.Should().BeFalse();

        var (byId, notFound) = await Sut().GetByIdAsync(existing.ItemId);
        notFound.Should().BeFalse();
        byId!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task H5_Query_ReturnsTenantPagedTotalCount()
    {
        var items = Enumerable.Range(0, 2).Select(i => Stored(name: $"Cfg {i}")).ToList();
        _repo.Setup(r => r.QueryAsync(TenantId, It.IsAny<QuerySignupLinkConfigurationsRequest>()))
            .ReturnsAsync((items, 5L));

        var (response, errors) = await Sut().QueryAsync(new QuerySignupLinkConfigurationsRequest { Page = 0, PageSize = 2 });

        errors.Should().BeNull();
        response!.Items.Should().HaveCount(2);
        response.TotalCount.Should().Be(5);
        _repo.Verify(r => r.QueryAsync(TenantId, It.IsAny<QuerySignupLinkConfigurationsRequest>()), Times.Once);
    }

    [Fact]
    public async Task C1_InactiveOrMissingClient_Rejected()
    {
        _oidc.Setup(o => o.GetByClientIdAsync("construct-web")).ReturnsAsync((OidcClientRegistrationInfo?)null);
        var missing = await Sut().CreateAsync(ValidCreate());
        missing.IsSuccess.Should().BeFalse();
        missing.Errors!["ClientId"].Should().Be("Client not found or inactive");
        _repo.Verify(r => r.InsertAsync(It.IsAny<SignupLinkConfiguration>()), Times.Never);

        _oidc.Setup(o => o.GetByClientIdAsync("construct-web"))
            .ReturnsAsync(new OidcClientRegistrationInfo("construct-web", ["https://construct.example.com/callback"], false));
        var inactive = await Sut().CreateAsync(ValidCreate());
        inactive.Errors!["ClientId"].Should().Be("Client not found or inactive");
    }

    [Fact]
    public async Task C2_UnregisteredRedirect_RejectedOnCreateAndPatch()
    {
        var create = await Sut().CreateAsync(ValidCreate(r => r.RedirectUri = "https://attacker.example.com/callback"));
        create.IsSuccess.Should().BeFalse();
        create.Errors!["RedirectUri"].Should().Be("RedirectUri is not registered for this client");
        _repo.Verify(r => r.InsertAsync(It.IsAny<SignupLinkConfiguration>()), Times.Never);

        var existing = Stored();
        _repo.Setup(r => r.GetByIdAsync(existing.ItemId, TenantId)).ReturnsAsync(existing);
        var patch = await Sut().UpdateAsync(existing.ItemId, new UpdateSignupLinkConfigurationRequest
        {
            RedirectUri = "https://attacker.example.com/callback"
        });
        patch.Errors!["RedirectUri"].Should().Be("RedirectUri is not registered for this client");
        _repo.Verify(r => r.ReplaceAsync(It.IsAny<SignupLinkConfiguration>()), Times.Never);
    }

    [Fact]
    public async Task C3_UnknownOrArchivedRole_Rejected()
    {
        _resources.Setup(r => r.GetNonArchivedRolesBySlugAsync("missing-role")).ReturnsAsync([]);
        var result = await Sut().CreateAsync(ValidCreate(r => r.DefaultRoles = ["missing-role"]));
        result.IsSuccess.Should().BeFalse();
        result.Errors!["DefaultRoles"].Should().Be("Unknown role: missing-role");
        _repo.Verify(r => r.InsertAsync(It.IsAny<SignupLinkConfiguration>()), Times.Never);
    }

    [Fact]
    public async Task C4_ProtocolRelativeForwardedTo_RejectedByValidatorErrors()
    {
        _createValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateSignupLinkConfigurationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([
                new ValidationFailure("DefaultForwardedTo", "ForwardedTo must be a relative path")
            ]));

        var result = await Sut().CreateAsync(ValidCreate(r => r.DefaultForwardedTo = "//evil.example.com"));
        result.IsSuccess.Should().BeFalse();
        result.Errors!["DefaultForwardedTo"].Should().Be("ForwardedTo must be a relative path");
    }

    [Fact]
    public async Task C5_OtherTenantId_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync("other-id", TenantId)).ReturnsAsync((SignupLinkConfiguration?)null);

        var (response, notFound) = await Sut().GetByIdAsync("other-id");
        notFound.Should().BeTrue();
        response.Should().BeNull();

        var patch = await Sut().UpdateAsync("other-id", new UpdateSignupLinkConfigurationRequest { Description = "x" });
        patch.IsSuccess.Should().BeFalse();
        patch.Errors!["ItemId"].Should().Be("Not found");
    }

    [Fact]
    public async Task Create_DuplicateName_Rejected()
    {
        _repo.Setup(r => r.FindByNameAsync(TenantId, "Construct Site Manager", null))
            .ReturnsAsync(Stored());

        var result = await Sut().CreateAsync(ValidCreate());
        result.Errors!["Name"].Should().Be("A configuration with this name already exists");
    }

    // ---- #593 RequireExistingUserPassword ----

    [Fact]
    public async Task H1_593_Create_WithoutRequireExistingUserPassword_StoresAndReturnsTrue()
    {
        SignupLinkConfiguration? saved = null;
        _repo.Setup(r => r.InsertAsync(It.IsAny<SignupLinkConfiguration>()))
            .Callback<SignupLinkConfiguration>(e => saved = e)
            .Returns(Task.CompletedTask);

        await Sut().CreateAsync(ValidCreate());

        saved!.RequireExistingUserPassword.Should().BeTrue();
        _repo.Setup(r => r.GetByIdAsync(saved.ItemId, TenantId)).ReturnsAsync(saved);
        var (response, _) = await Sut().GetByIdAsync(saved.ItemId);
        response!.RequireExistingUserPassword.Should().BeTrue();
    }

    [Fact]
    public async Task H2_593_Create_WithFalse_StoresAndReturnsFalse()
    {
        SignupLinkConfiguration? saved = null;
        _repo.Setup(r => r.InsertAsync(It.IsAny<SignupLinkConfiguration>()))
            .Callback<SignupLinkConfiguration>(e => saved = e)
            .Returns(Task.CompletedTask);

        await Sut().CreateAsync(ValidCreate(r => r.RequireExistingUserPassword = false));

        saved!.RequireExistingUserPassword.Should().BeFalse();
        _repo.Setup(r => r.GetByIdAsync(saved.ItemId, TenantId)).ReturnsAsync(saved);
        var (response, _) = await Sut().GetByIdAsync(saved.ItemId);
        response!.RequireExistingUserPassword.Should().BeFalse();
    }

    [Fact]
    public async Task H4_593_Patch_ChangesOnlyRequireExistingUserPassword_WhenSupplied()
    {
        var existing = Stored();
        existing.RequireExistingUserPassword = false;
        _repo.Setup(r => r.GetByIdAsync(existing.ItemId, TenantId)).ReturnsAsync(existing);

        // Omitted: unchanged.
        var rename = await Sut().UpdateAsync(existing.ItemId, new UpdateSignupLinkConfigurationRequest { Name = "Renamed" });
        rename.IsSuccess.Should().BeTrue();
        existing.RequireExistingUserPassword.Should().BeFalse();

        // Supplied: only that value changes.
        var patch = await Sut().UpdateAsync(existing.ItemId, new UpdateSignupLinkConfigurationRequest { RequireExistingUserPassword = true });
        patch.IsSuccess.Should().BeTrue();
        existing.RequireExistingUserPassword.Should().BeTrue();
        existing.Name.Should().Be("Renamed");
        existing.SignInAfterActivation.Should().BeFalse();

        var (response, _) = await Sut().GetByIdAsync(existing.ItemId);
        response!.RequireExistingUserPassword.Should().BeTrue();
    }

    [Fact]
    public async Task H3_593_Query_MapsTheFlag()
    {
        var on = Stored("on");
        var off = Stored("off");
        off.RequireExistingUserPassword = false;
        _repo.Setup(r => r.QueryAsync(TenantId, It.IsAny<QuerySignupLinkConfigurationsRequest>()))
            .ReturnsAsync((new List<SignupLinkConfiguration> { on, off }, 2L));

        var (response, _) = await Sut().QueryAsync(new QuerySignupLinkConfigurationsRequest { Page = 0, PageSize = 20 });

        response!.Items.Select(i => i.RequireExistingUserPassword).Should().Equal(true, false);
    }

    [Fact]
    public void H3_593_LegacyDocumentWithoutTheElement_ReadsAsTrue()
    {
        var document = new MongoDB.Bson.BsonDocument
        {
            { "_id", Guid.NewGuid().ToString() },
            { "TenantId", TenantId },
            { "Name", "legacy" },
            { "CredentialMode", 0 },
            { "SignInAfterActivation", false }
        };

        var configuration = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<SignupLinkConfiguration>(document);
        configuration.RequireExistingUserPassword.Should().BeTrue();

        document["RequireExistingUserPassword"] = false;
        MongoDB.Bson.Serialization.BsonSerializer.Deserialize<SignupLinkConfiguration>(document)
            .RequireExistingUserPassword.Should().BeFalse();
    }

    [Fact]
    public void H3_593_LegacyLinkWithoutTheElement_ReadsAsTrue()
    {
        var document = new MongoDB.Bson.BsonDocument
        {
            { "_id", Guid.NewGuid().ToString() },
            { "TenantId", TenantId },
            { "Email", "jo@acme.io" }
        };

        MongoDB.Bson.Serialization.BsonSerializer.Deserialize<SignupLink>(document)
            .RequireExistingUserPassword.Should().BeTrue();
    }

    private static SignupLinkConfiguration Stored(string? name = null) => new()
    {
        ItemId = Guid.NewGuid().ToString(),
        TenantId = TenantId,
        Name = name ?? "Construct Site Manager",
        DefaultRoles = ["site-manager"],
        DefaultPermissions = [],
        ClientId = "construct-web",
        RedirectUri = "https://construct.example.com/callback",
        DefaultForwardedTo = "/projects",
        CredentialMode = SignupLinkCredentialMode.PasswordRequired,
        DefaultLifetimeMinutes = 1440,
        IsActive = true,
        CreatedDate = DateTime.UtcNow.AddMinutes(-5),
        LastUpdatedDate = DateTime.UtcNow.AddMinutes(-5),
        CreatedBy = ActorId,
        LastUpdatedBy = ActorId
    };
}
