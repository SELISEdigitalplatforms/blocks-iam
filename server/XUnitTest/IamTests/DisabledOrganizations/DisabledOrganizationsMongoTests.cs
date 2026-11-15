using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared.Services;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.DisabledOrganizations
{
    /// <summary>
    /// Spec #668 against the local test Mongo (one throwaway database per test):
    /// <see cref="ResourceRepository.GetOrganizationStatusesAsync"/> (one policy-free read of id and status) and the
    /// <c>AllowNoOrgSignInForDisabledOrganizations</c> field through the tenant-configuration store (C8, §6.5).
    /// </summary>
    public sealed class DisabledOrganizationsMongoTests : IDisposable
    {
        private readonly RealMongo _mongo = new();

        public void Dispose() => _mongo.Dispose();

        // ---------- GetOrganizationStatusesAsync ----------

        private IMongoCollection<Organization> Organizations => _mongo.Database.GetCollection<Organization>("Organizations");

        /// <summary>The repository with the real collection, and a strict data-access mock that fails if consulted.</summary>
        private ResourceRepository Repository(Mock<IIamDataAccess> dataAccess)
        {
            var iam = new Mock<IIdentityAccessManagementRepository>();
            iam.Setup(r => r.GetCollection<Organization>()).Returns(Organizations);
            return new ResourceRepository(iam.Object, dataAccess.Object);
        }

        private Task SeedAsync(params (string Id, bool IsDisabled)[] organizations) =>
            Organizations.InsertManyAsync(organizations.Select(o => new Organization { ItemId = o.Id, Name = "name-" + o.Id, IsDisabled = o.IsDisabled }));

        [RealMongoFact]
        public async Task Statuses_IdToIsDisabled_MissingIdsOmitted_WithoutTheRowPolicy()
        {
            await SeedAsync(("org-a", false), ("org-d", true), ("org-other", true));
            var dataAccess = new Mock<IIamDataAccess>(MockBehavior.Strict);

            var statuses = await Repository(dataAccess).GetOrganizationStatusesAsync(["org-a", "org-d", "org-x"]);

            statuses.Should().HaveCount(2);
            statuses["org-a"].Should().BeFalse();
            statuses["org-d"].Should().BeTrue();
            statuses.Should().NotContainKey("org-x");
            statuses.Should().NotContainKey("org-other");
            dataAccess.VerifyNoOtherCalls();
        }

        [RealMongoFact]
        public async Task Statuses_BlanksAndDuplicatesIgnored()
        {
            await SeedAsync(("org-a", false), ("org-d", true));

            var statuses = await Repository(new Mock<IIamDataAccess>(MockBehavior.Strict))
                .GetOrganizationStatusesAsync(["org-a", "", "  ", "org-a", "org-d", "org-d"]);

            statuses.Keys.Should().BeEquivalentTo(["org-a", "org-d"]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task Statuses_EmptyOrBlankInput_NoQuery(int shape)
        {
            // Strict: any collection access would throw.
            var iam = new Mock<IIdentityAccessManagementRepository>(MockBehavior.Strict);
            var repository = new ResourceRepository(iam.Object, new Mock<IIamDataAccess>(MockBehavior.Strict).Object);
            IReadOnlyCollection<string> input = shape == 0 ? Array.Empty<string>() : new[] { "", " " };

            var statuses = await repository.GetOrganizationStatusesAsync(input);

            statuses.Should().BeEmpty();
            iam.VerifyNoOtherCalls();
        }

        // ---------- tenant configuration field ----------

        private IMongoCollection<BsonDocument> Identity => _mongo.Database.GetCollection<BsonDocument>("IdentityConfigurations");

        private TenantConfigurationStore Store() => new(_mongo.ProviderFor());

        private IdentityAccessManagementRepository Iam() =>
            new(_mongo.ProviderFor(), new Mock<IBlocksSecret>().Object, NullLogger<IdentityAccessManagementRepository>.Instance, new XUnitTest.TestDoubles.PassThroughIamDataAccess());

        private Task SeedConfigAsync(bool? allowNoOrg)
        {
            var field = new BsonDocument
            {
                { "_id", "tc-field" },
                { "IsMultiOrgEnabled", true },
                { "DefaultRolesForNewUserOnSignUp", new BsonArray(new[] { "member" }) },
                { "DefaultPermissionsForNewUserOnSignUp", new BsonArray() },
                { "LastUpdatedDate", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
            };
            if (allowNoOrg.HasValue)
            {
                field["AllowNoOrgSignInForDisabledOrganizations"] = allowNoOrg.Value;
            }

            return Identity.InsertOneAsync(new BsonDocument { { "_id", ObjectId.GenerateNewId() }, { "TenantConfiguration", field } });
        }

        [RealMongoFact]
        public async Task C8_DocumentWithoutTheField_ReadsFalse()
        {
            await SeedConfigAsync(allowNoOrg: null);

            (await Store().ReadAsync())!.AllowNoOrgSignInForDisabledOrganizations.Should().BeFalse();
        }

        [RealMongoFact]
        public async Task H6_StoredTrue_ReadsTrue()
        {
            await SeedConfigAsync(allowNoOrg: true);

            (await Store().ReadAsync())!.AllowNoOrgSignInForDisabledOrganizations.Should().BeTrue();
        }

        [RealMongoFact]
        public async Task H6_SaveOrganizationConfig_SetsTheField_AndClearsIt()
        {
            await SeedConfigAsync(allowNoOrg: null);

            await Iam().SaveOrganizationConfigAsync(new IamTenantConfiguration { IsMultiOrgEnabled = true, AllowNoOrgSignInForDisabledOrganizations = true });
            var tc = (await Identity.Find(Builders<BsonDocument>.Filter.Empty).FirstAsync())["TenantConfiguration"].AsBsonDocument;
            tc["AllowNoOrgSignInForDisabledOrganizations"].AsBoolean.Should().BeTrue();
            (await Store().ReadAsync())!.AllowNoOrgSignInForDisabledOrganizations.Should().BeTrue();

            await Iam().SaveOrganizationConfigAsync(new IamTenantConfiguration { IsMultiOrgEnabled = true, AllowNoOrgSignInForDisabledOrganizations = false });
            (await Store().ReadAsync())!.AllowNoOrgSignInForDisabledOrganizations.Should().BeFalse();
        }

        [RealMongoFact]
        public async Task C9_MultiOrgSwitchedOff_KeepsTheStoredValue()
        {
            await SeedConfigAsync(allowNoOrg: true);
            var stored = await Store().ReadAsync();
            stored!.IsMultiOrgEnabled = false;

            await Iam().SaveOrganizationConfigAsync(stored);

            var config = await Store().ReadAsync();
            config!.IsMultiOrgEnabled.Should().BeFalse();
            config.AllowNoOrgSignInForDisabledOrganizations.Should().BeTrue();
        }
    }
}
