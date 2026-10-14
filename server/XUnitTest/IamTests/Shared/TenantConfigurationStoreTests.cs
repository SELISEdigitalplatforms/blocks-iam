using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.IamTests.Shared;

/// <summary>
/// SPEC1 3.3: the tenant configuration lives in the field <c>TenantConfiguration</c> of the one
/// <c>IdentityConfigurations</c> document, with the old <c>TenantConfigurations</c> collection as fallback.
/// These tests run on the local test Mongo (one throwaway database each).
/// </summary>
public class TenantConfigurationStoreTests : IDisposable
{
    private readonly RealMongo _mongo = new();

    public void Dispose() => _mongo.Dispose();

    private TenantConfigurationStore Store() => new(_mongo.ProviderFor());

    private IdentityAccessManagementRepository Iam() =>
        new(_mongo.ProviderFor(), new Mock<IBlocksSecret>().Object, NullLogger<IdentityAccessManagementRepository>.Instance);

    private IMongoCollection<BsonDocument> Identity => _mongo.Database.GetCollection<BsonDocument>("IdentityConfigurations");
    private IMongoCollection<BsonDocument> Legacy => _mongo.Database.GetCollection<BsonDocument>("TenantConfigurations");

    private static BsonDocument FieldValue(bool multiOrg = false, string[]? roles = null, DateTime? updated = null) => new()
    {
        { "_id", "tc-field" },
        { "IsEmailPasswordSignUpEnabled", true },
        { "IsSSoSignUpEnabled", false },
        { "DefaultRolesForNewUserOnSignUp", new BsonArray(roles ?? ["member", "manager"]) },
        { "DefaultPermissionsForNewUserOnSignUp", new BsonArray(new[] { "a::read", "b::read" }) },
        { "AllowOrgCreationFromCloud", true },
        { "IsMultiOrgEnabled", multiOrg },
        { "LastUpdatedDate", updated ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
    };

    private static BsonDocument LegacyDoc(bool multiOrg = false) => new()
    {
        { "_id", "tc-legacy" },
        { "IsEmailPasswordSignUpEnabled", false },
        { "DefaultRolesForNewUserOnSignUp", new BsonArray(new[] { "old-role", "manager" }) },
        { "DefaultPermissionsForNewUserOnSignUp", new BsonArray(new[] { "old::read" }) },
        { "IsMultiOrgEnabled", multiOrg },
    };

    private Task SeedIdentityAsync(BsonDocument? field, string extra = "keep") =>
        Identity.InsertOneAsync(new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "AccessTokenValidForNumberMinutes", 7 },
            { "Extra", extra },
            { "TenantConfiguration", field is null ? BsonNull.Value : field },
        }.Also(d => { if (field is null) d.Remove("TenantConfiguration"); }));

    // ---------- read ----------

    [RealMongoFact]
    public async Task Read_FieldPresent_ReturnsTheFieldValues_NotTheOldCollection()
    {
        await SeedIdentityAsync(FieldValue(multiOrg: true));
        await Legacy.InsertOneAsync(LegacyDoc());

        var config = await Store().ReadAsync();

        config.Should().NotBeNull();
        config!.ItemId.Should().Be("tc-field");
        config.IsEmailPasswordSignUpEnabled.Should().BeTrue();
        config.IsMultiOrgEnabled.Should().BeTrue();
        config.DefaultRolesForNewUserOnSignUp.Should().Equal("member", "manager");
        config.DefaultPermissionsForNewUserOnSignUp.Should().Equal("a::read", "b::read");
        config.AllowOrgCreationFromCloud.Should().BeTrue();
    }

    [RealMongoFact]
    public async Task Read_FieldMissing_FallsBackToTheFirstOldDoc()
    {
        await SeedIdentityAsync(null);
        await Legacy.InsertOneAsync(LegacyDoc());

        var config = await Store().ReadAsync();

        config!.ItemId.Should().Be("tc-legacy");
        config.DefaultRolesForNewUserOnSignUp.Should().Equal("old-role", "manager");
    }

    [RealMongoFact]
    public async Task Read_NoIdentityConfigurationsDoc_FallsBackToTheOldCollection()
    {
        await Legacy.InsertOneAsync(LegacyDoc());

        (await Store().ReadAsync())!.ItemId.Should().Be("tc-legacy");
    }

    [RealMongoFact]
    public async Task Read_NothingAnywhere_ReturnsNull_AndCreatesNoCollection()
    {
        (await Store().ReadAsync()).Should().BeNull();

        var names = await (await _mongo.Database.ListCollectionNamesAsync()).ToListAsync();
        names.Should().NotContain("TenantConfigurations");
    }

    [RealMongoFact]
    public async Task Read_TwoIdentityDocs_UsesTheFirst_AsToday()
    {
        // C4: the app reads the first doc (the migration blocks such a DB).
        await Identity.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "TenantConfiguration", FieldValue() } });
        await Identity.InsertOneAsync(new BsonDocument { { "_id", 2 }, { "TenantConfiguration", new BsonDocument { { "_id", "second" } } } });

        (await Store().ReadAsync())!.ItemId.Should().Be("tc-field");
    }

    [RealMongoFact]
    public async Task Read_AnotherDatabase_FieldOnly_AfterCleanup()
    {
        // The cross-database overload used by the permission propagator.
        await SeedIdentityAsync(FieldValue(multiOrg: true));

        var config = await Store().ReadAsync(_mongo.Database);

        config!.IsMultiOrgEnabled.Should().BeTrue();
    }

    [RealMongoFact]
    public async Task Read_AnotherDatabase_FieldMissing_ReadsItsOldCollection()
    {
        await SeedIdentityAsync(null);
        await Legacy.InsertOneAsync(LegacyDoc(multiOrg: true));

        (await Store().ReadAsync(_mongo.Database))!.IsMultiOrgEnabled.Should().BeTrue();
    }

    [RealMongoFact]
    public async Task Read_AnotherDatabase_Neither_ReturnsNull()
    {
        (await Store().ReadAsync(_mongo.Database)).Should().BeNull();
    }

    // ---------- write ----------

    [RealMongoFact]
    public async Task SaveSignUpSetting_WritesTheField_SetsLastUpdatedDate_LeavesTheRestAndTheOldCollection()
    {
        await SeedIdentityAsync(FieldValue());
        await Legacy.InsertOneAsync(LegacyDoc());
        var before = DateTime.UtcNow.AddSeconds(-1);

        await Iam().SaveSignUpSettingAsync(new TenantConfiguration
        {
            IsEmailPasswordSignUpEnabled = false,
            IsSSoSignUpEnabled = true,
            DefaultRolesForNewUserOnSignUp = ["only-role"],
            DefaultPermissionsForNewUserOnSignUp = ["only::perm"],
            LastUpdatedBy = "admin-1",
            LastUpdatedDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        var doc = await Identity.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync();
        var tc = doc["TenantConfiguration"].AsBsonDocument;
        tc["IsEmailPasswordSignUpEnabled"].AsBoolean.Should().BeFalse();
        tc["IsSSoSignUpEnabled"].AsBoolean.Should().BeTrue();
        tc["DefaultRolesForNewUserOnSignUp"].AsBsonArray.Select(v => v.AsString).Should().Equal("only-role");
        tc["LastUpdatedBy"].AsString.Should().Be("admin-1");
        tc["LastUpdatedDate"].ToUniversalTime().Should().BeAfter(before);   // always "now", never the caller's value
        tc["AllowOrgCreationFromCloud"].AsBoolean.Should().BeTrue();        // untouched field of the value
        doc["Extra"].AsString.Should().Be("keep");
        doc["AccessTokenValidForNumberMinutes"].AsInt32.Should().Be(7);

        var legacy = await Legacy.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync();
        legacy["IsEmailPasswordSignUpEnabled"].AsBoolean.Should().BeFalse();
        legacy["DefaultRolesForNewUserOnSignUp"].AsBsonArray.Select(v => v.AsString).Should().Equal("old-role", "manager");
    }

    [RealMongoFact]
    public async Task SaveSignUpSetting_FieldMissing_WritesTheOldCollection_AsToday()
    {
        await SeedIdentityAsync(null);
        await Legacy.InsertOneAsync(LegacyDoc());

        await Iam().SaveSignUpSettingAsync(new TenantConfiguration
        {
            IsEmailPasswordSignUpEnabled = true,
            DefaultRolesForNewUserOnSignUp = ["x"],
            DefaultPermissionsForNewUserOnSignUp = [],
            LastUpdatedBy = "admin-1",
            LastUpdatedDate = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc),
        });

        var legacy = await Legacy.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync();
        legacy["IsEmailPasswordSignUpEnabled"].AsBoolean.Should().BeTrue();
        legacy["LastUpdatedDate"].ToUniversalTime().Should().Be(new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc)); // caller's value, as before
        (await Identity.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync()).Contains("TenantConfiguration").Should().BeFalse();
    }

    [RealMongoFact]
    public async Task SaveSignUpSetting_FieldIsNull_WritesTheOldCollection_LikeTheRead()
    {
        await Identity.InsertOneAsync(new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "TenantConfiguration", BsonNull.Value } });
        await Legacy.InsertOneAsync(LegacyDoc());

        await Iam().SaveSignUpSettingAsync(new TenantConfiguration { IsEmailPasswordSignUpEnabled = true });

        (await Legacy.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync())["IsEmailPasswordSignUpEnabled"].AsBoolean.Should().BeTrue();
        (await Identity.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync())["TenantConfiguration"].IsBsonNull.Should().BeTrue();
    }

    [RealMongoFact]
    public async Task SaveSignUpSetting_NoIdentityDoc_WritesTheOldCollection()
    {
        await Legacy.InsertOneAsync(LegacyDoc());

        await Iam().SaveSignUpSettingAsync(new TenantConfiguration { IsEmailPasswordSignUpEnabled = true });

        (await Legacy.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync())["IsEmailPasswordSignUpEnabled"].AsBoolean.Should().BeTrue();
        (await Identity.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty)).Should().Be(0);
    }

    [RealMongoFact]
    public async Task SaveOrganizationConfig_WritesTheField_AndNeverUpsertsTheOldCollection()
    {
        await SeedIdentityAsync(FieldValue());

        await Iam().SaveOrganizationConfigAsync(new TenantConfiguration
        {
            ItemId = "ignored-on-update",
            IsMultiOrgEnabled = true,
            AllowOrgCreationFromPortal = true,
            IsOrgNameUniquenessEnabled = true,
            ConsentForMultiOrgEnable = true,
            DefaultPermissionsForNewUserOnSignUp = ["z::read"],
            LastUpdatedBy = "admin-2",
        });

        var tc = (await Identity.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync())["TenantConfiguration"].AsBsonDocument;
        tc["_id"].AsString.Should().Be("tc-field");                       // identity of the value kept ($setOnInsert dropped)
        tc["IsMultiOrgEnabled"].AsBoolean.Should().BeTrue();
        tc["AllowOrgCreationFromPortal"].AsBoolean.Should().BeTrue();
        tc["IsOrgNameUniquenessEnabled"].AsBoolean.Should().BeTrue();
        tc["DefaultPermissionsForNewUserOnSignUp"].AsBsonArray.Select(v => v.AsString).Should().Equal("z::read");
        tc["DefaultRolesForNewUserOnSignUp"].AsBsonArray.Select(v => v.AsString).Should().Equal("member", "manager"); // not in this write
        (await Legacy.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty)).Should().Be(0);
    }

    [RealMongoFact]
    public async Task SaveOrganizationConfig_NothingAnywhere_UpsertsTheOldCollection_AsToday()
    {
        await Iam().SaveOrganizationConfigAsync(new TenantConfiguration { ItemId = "new-1", IsMultiOrgEnabled = true });

        var legacy = await Legacy.Find(Builders<BsonDocument>.Filter.Empty).SingleAsync();
        legacy["_id"].AsString.Should().Be("new-1");
        legacy["IsMultiOrgEnabled"].AsBoolean.Should().BeTrue();
    }

    [RealMongoFact]
    public async Task PullSignUpDefaultRole_PullsFromTheFieldRolesOnly_AndSetsLastUpdatedDate()
    {
        await SeedIdentityAsync(FieldValue(roles: ["member", "manager"]));
        await Legacy.InsertOneAsync(LegacyDoc());

        (await Iam().PullSignUpDefaultRoleAsync("manager")).Should().BeTrue();

        var tc = (await Identity.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync())["TenantConfiguration"].AsBsonDocument;
        tc["DefaultRolesForNewUserOnSignUp"].AsBsonArray.Select(v => v.AsString).Should().Equal("member");
        tc["DefaultPermissionsForNewUserOnSignUp"].AsBsonArray.Select(v => v.AsString).Should().Equal("a::read", "b::read");
        tc["LastUpdatedDate"].ToUniversalTime().Should().BeAfter(DateTime.UtcNow.AddSeconds(-5));
        (await Legacy.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync())["DefaultRolesForNewUserOnSignUp"]
            .AsBsonArray.Select(v => v.AsString).Should().Equal("old-role", "manager");
    }

    [RealMongoFact]
    public async Task PullSignUpDefaultPermission_PullsFromTheFieldPermissionsOnly()
    {
        await SeedIdentityAsync(FieldValue());

        (await Iam().PullSignUpDefaultPermissionAsync("a::read")).Should().BeTrue();

        var tc = (await Identity.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync())["TenantConfiguration"].AsBsonDocument;
        tc["DefaultPermissionsForNewUserOnSignUp"].AsBsonArray.Select(v => v.AsString).Should().Equal("b::read");
        tc["DefaultRolesForNewUserOnSignUp"].AsBsonArray.Select(v => v.AsString).Should().Equal("member", "manager");
    }

    [RealMongoFact]
    public async Task Pull_FieldMissing_PullsFromTheOldCollection()
    {
        await SeedIdentityAsync(null);
        await Legacy.InsertOneAsync(LegacyDoc());

        (await Iam().PullSignUpDefaultRoleAsync("manager")).Should().BeTrue();

        (await Legacy.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync())["DefaultRolesForNewUserOnSignUp"]
            .AsBsonArray.Select(v => v.AsString).Should().Equal("old-role");
    }

    [RealMongoFact]
    public async Task Pull_MatchingNothing_IsSuccessNotFailure()
    {
        // A tenant with no configuration anywhere: an acknowledged write of zero documents.
        (await Iam().PullSignUpDefaultRoleAsync("manager")).Should().BeTrue();
        (await Iam().PullSignUpDefaultPermissionAsync("a::read")).Should().BeTrue();

        await SeedIdentityAsync(FieldValue());
        (await Iam().PullSignUpDefaultRoleAsync("not-a-default")).Should().BeTrue();
    }

    [RealMongoFact]
    public async Task GetTenantConfiguration_ThroughTheRepository_ReturnsTheFieldValue()
    {
        await SeedIdentityAsync(FieldValue(multiOrg: true));

        var config = await Iam().GetTenantConfigurationAsync();

        config.IsMultiOrgEnabled.Should().BeTrue();
        config.DefaultRolesForNewUserOnSignUp.Should().Equal("member", "manager");
    }

    // ---------- auth/config save keeps the merged field and unmapped fields (H6, C15) ----------

    [RealMongoFact]
    public async Task UpdateAuthenticationConfiguration_KeepsTheTenantConfigurationField_AndTheRootAllowedApis()
    {
        var id = ObjectId.GenerateNewId();
        await Identity.InsertOneAsync(new BsonDocument
        {
            { "_id", id },
            { "RefreshTokenValidForNumberMinutes", 30 },
            { "AllowedApis", new BsonArray(new[] { "/iam/*", "/idp/*" }) },   // read by Genesis onboarding, not mapped by the class
            { "TenantConfiguration", FieldValue(multiOrg: true) },
        });
        var auth = new AuthenticationRepository(_mongo.ProviderFor(), new OidcDiscoveryClient(new Mock<IHttpClientFactory>().Object), new Mock<IKeyValueStore>().Object);

        var config = await auth.GetAuthenticationConfigurationAsync();
        config.RefreshTokenValidForNumberMinutes = 45;
        config.PasswordPolicyMinLength = 12;
        config.PublicCertificatePath = null!;
        await auth.UpdateAuthenticationConfigurationAsync(config);

        var doc = await Identity.Find(Builders<BsonDocument>.Filter.Empty).SingleAsync();
        doc["_id"].AsObjectId.Should().Be(id);
        doc["RefreshTokenValidForNumberMinutes"].AsInt32.Should().Be(45);   // mapped values saved as before
        doc["PasswordPolicyMinLength"].AsInt32.Should().Be(12);
        doc["AllowedApis"].AsBsonArray.Select(v => v.AsString).Should().Equal("/iam/*", "/idp/*");
        doc["TenantConfiguration"].AsBsonDocument["IsMultiOrgEnabled"].AsBoolean.Should().BeTrue();
        doc["TenantConfiguration"].AsBsonDocument["DefaultRolesForNewUserOnSignUp"].AsBsonArray.Should().HaveCount(2);
    }

    [RealMongoFact]
    public async Task UpdateAuthenticationConfiguration_SavesEveryMappedField_LikeTheOldReplace()
    {
        var id = ObjectId.GenerateNewId();
        await Identity.InsertOneAsync(new BsonDocument { { "_id", id } });
        var auth = new AuthenticationRepository(_mongo.ProviderFor(), new OidcDiscoveryClient(new Mock<IHttpClientFactory>().Object), new Mock<IKeyValueStore>().Object);
        var config = new IdentityConfiguration
        {
            ItemId = id,
            AllowedGrantTypes = ["password", "authorization_code"],
            IsOidcEnabled = true,
            AccountActionBaseUrl = "https://acct.example",
            PasswordStrengthCheckerMessage = "msg",
            LogoutOnPasswordChange = false,
            CollectPasswordOnActivation = false,
        };

        await auth.UpdateAuthenticationConfigurationAsync(config);

        var back = await auth.GetAuthenticationConfigurationAsync();
        back.AllowedGrantTypes.Should().Equal("password", "authorization_code");
        back.IsOidcEnabled.Should().BeTrue();
        back.AccountActionBaseUrl.Should().Be("https://acct.example");
        back.PasswordStrengthCheckerMessage.Should().Be("msg");
        back.LogoutOnPasswordChange.Should().BeFalse();
        back.CollectPasswordOnActivation.Should().BeFalse();
        back.AccessTokenValidForNumberMinutes.Should().Be(IdentityConfiguration.DefaultAccessTokenValidForNumberMinutes);
    }
}

internal static class BsonDocumentExtensions
{
    public static BsonDocument Also(this BsonDocument d, Action<BsonDocument> a) { a(d); return d; }
}
