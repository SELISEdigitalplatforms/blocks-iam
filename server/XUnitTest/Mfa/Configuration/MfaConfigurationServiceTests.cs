using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Moq;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.Mfa.Configuration
{
    public class MfaConfigurationServiceTests
    {
        private static MfaConfiguration BuildConfig(
            bool enableMfa = true,
            int backupCodesCount = 10,
            List<UserMfaType>? types = null,
            List<string>? requiredRoles = null,
            List<string>? exemptRoles = null,
            MfaTemplate? template = null)
        {
            return new MfaConfiguration
            {
                EnableMfa = enableMfa,
                BackupCodesCount = backupCodesCount,
                UserMfaTypes = types ?? new List<UserMfaType> { UserMfaType.Email },
                MfaRequiredRoles = requiredRoles ?? new List<string> { "admin" },
                MfaExemptRoles = exemptRoles ?? new List<string> { "service" },
                MfaTemplate = template ?? new MfaTemplate { TemplateName = "MfaViaEmail", TemplateId = "t-1" }
            };
        }

        private static Mock<IKeyValueStore> Kv(MfaConfiguration? stored = null)
        {
            var kv = new Mock<IKeyValueStore>();
            kv.Setup(k => k.GetAsync<MfaConfiguration>(MfaConfigurationService.StoreKey, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stored);
            return kv;
        }

        private static Mock<IMfaManagementRepository> OldDoc(MfaConfiguration? oldDoc)
        {
            var repo = new Mock<IMfaManagementRepository>();
            repo.Setup(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(oldDoc);
            return repo;
        }

        [Fact]
        public async Task GetAsync_WhenNoConfigInRepo_ReturnsDefaultEmptyConfiguration()
        {
            var repo = new Mock<IMfaManagementRepository>();
            repo.Setup(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((MfaConfiguration?)null);

            var service = new MfaConfigurationService(repo.Object, Kv().Object);
            var result = await service.GetAsync();

            result.Should().NotBeNull();
            result!.EnableMfa.Should().BeFalse();
            result.UserMfaType.Should().BeEmpty();
            result.MfaTemplate.Should().NotBeNull();
        }

        [Fact]
        public async Task GetAsync_WhenConfigExists_MapsAllFields()
        {
            var repoConfig = BuildConfig();
            var repo = new Mock<IMfaManagementRepository>();
            repo.Setup(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(repoConfig);

            var service = new MfaConfigurationService(repo.Object, Kv().Object);
            var result = await service.GetAsync();

            result.Should().NotBeNull();
            result!.EnableMfa.Should().BeTrue();
            result.UserMfaType.Should().ContainSingle().Which.Should().Be(UserMfaType.Email);
            result.MfaTemplate!.TemplateName.Should().Be("MfaViaEmail");
            result.MfaRequiredRoles.Should().ContainSingle().Which.Should().Be("admin");
            result.MfaExemptRoles.Should().ContainSingle().Which.Should().Be("service");
            result.BackupCodesCount.Should().Be(10);
        }

        [Fact]
        public async Task GetAsync_WhenBackupCodesCountIsZero_DefaultsTo10()
        {
            var repoConfig = BuildConfig(backupCodesCount: 0);
            var repo = new Mock<IMfaManagementRepository>();
            repo.Setup(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(repoConfig);

            var service = new MfaConfigurationService(repo.Object, Kv().Object);
            var result = await service.GetAsync();

            result!.BackupCodesCount.Should().Be(10);
        }

        [Fact]
        public async Task GetAsync_NullCollections_AreCoalescedToEmptyLists()
        {
            var repoConfig = new MfaConfiguration
            {
                EnableMfa = true,
                UserMfaTypes = null,
                MfaRequiredRoles = null,
                MfaExemptRoles = null
            };
            var repo = new Mock<IMfaManagementRepository>();
            repo.Setup(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(repoConfig);

            var service = new MfaConfigurationService(repo.Object, Kv().Object);
            var result = await service.GetAsync();

            result!.UserMfaType.Should().BeEmpty();
            result.MfaRequiredRoles.Should().BeEmpty();
            result.MfaExemptRoles.Should().BeEmpty();
        }

        [Fact]
        public async Task SaveAsync_WhenNoExisting_CreatesNewWithGeneratedIdAndDefaults()
        {
            var repo = new Mock<IMfaManagementRepository>();
            repo.Setup(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((MfaConfiguration?)null);

            MfaConfiguration? upserted = null;
            var kv = Kv();
            kv.Setup(k => k.SetAsync(MfaConfigurationService.StoreKey, It.IsAny<MfaConfiguration>(), true, It.IsAny<CancellationToken>()))
                .Callback<string, MfaConfiguration, bool, CancellationToken>((_, c, _, _) => upserted = c)
                .Returns(Task.CompletedTask);

            var service = new MfaConfigurationService(repo.Object, kv.Object);
            await service.SaveAsync(new global::Mfa.DomainService.Configuration.Configuration
            {
                EnableMfa = true,
                UserMfaType = new List<UserMfaType> { UserMfaType.TOTP }
            });

            upserted.Should().NotBeNull();
            upserted!.EnableMfa.Should().BeTrue();
            upserted.UserMfaTypes.Should().ContainSingle().Which.Should().Be(UserMfaType.TOTP);
            upserted.Name.Should().Be("Default");
            upserted.ItemId.Should().NotBeNullOrEmpty();
            upserted.CreatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
            upserted.LastUpdatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task SaveAsync_WhenExisting_UpdatesAllFields_AndLastUpdatedDate()
        {
            var existing = new MfaConfiguration
            {
                ItemId = "existing-id",
                Name = "Default",
                EnableMfa = false,
                CreatedDate = DateTime.UtcNow.AddDays(-1),
                LastUpdatedDate = DateTime.UtcNow.AddDays(-1)
            };

            var repo = new Mock<IMfaManagementRepository>();
            repo.Setup(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(existing);

            MfaConfiguration? upserted = null;
            var kv = Kv();
            kv.Setup(k => k.SetAsync(MfaConfigurationService.StoreKey, It.IsAny<MfaConfiguration>(), true, It.IsAny<CancellationToken>()))
                .Callback<string, MfaConfiguration, bool, CancellationToken>((_, c, _, _) => upserted = c)
                .Returns(Task.CompletedTask);

            var service = new MfaConfigurationService(repo.Object, kv.Object);
            await service.SaveAsync(new global::Mfa.DomainService.Configuration.Configuration
            {
                EnableMfa = true,
                UserMfaType = new List<UserMfaType> { UserMfaType.Sms },
                BackupCodesCount = 25
            });

            upserted.Should().NotBeNull();
            upserted!.EnableMfa.Should().BeTrue();
            upserted.UserMfaTypes.Should().ContainSingle().Which.Should().Be(UserMfaType.Sms);
            upserted.BackupCodesCount.Should().Be(25);
            upserted.LastUpdatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        // ---------- KeyValueStores "mfa-config" (SPEC1 3.4) ----------

        [Fact]
        public async Task GetAsync_KvHit_UsesTheStoredValue_AndNeverReadsTheOldDoc()
        {
            var repo = OldDoc(BuildConfig(enableMfa: false));
            var service = new MfaConfigurationService(repo.Object, Kv(BuildConfig(enableMfa: true)).Object);

            var result = await service.GetAsync();

            result!.EnableMfa.Should().BeTrue();
            repo.Verify(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetAsync_KvMiss_FallsBackToTheOldDoc()
        {
            var service = new MfaConfigurationService(OldDoc(BuildConfig(enableMfa: true)).Object, Kv().Object);

            (await service.GetAsync())!.EnableMfa.Should().BeTrue();
        }

        [Fact]
        public async Task GetAsync_KvAndOldDocMiss_ReturnsTheDefaultObject()
        {
            var service = new MfaConfigurationService(OldDoc(null).Object, Kv().Object);

            var result = await service.GetAsync();

            result!.EnableMfa.Should().BeFalse();
            result.UserMfaType.Should().BeEmpty();
        }

        [Fact]
        public async Task GetAsync_KvThrows_Throws_NeverReturnsMfaOff()
        {
            var kv = new Mock<IKeyValueStore>();
            kv.Setup(k => k.GetAsync<MfaConfiguration>(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("kv down"));
            var repo = OldDoc(BuildConfig(enableMfa: true));
            var service = new MfaConfigurationService(repo.Object, kv.Object);

            await service.Invoking(s => s.GetAsync()).Should().ThrowAsync<InvalidOperationException>();
            repo.Verify(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SaveAsync_WritesTheStore_UnderTheContextTenant_AndNeverTheOldCollection()
        {
            var repo = OldDoc(null);
            var kv = Kv();
            var service = new MfaConfigurationService(repo.Object, kv.Object);

            await service.SaveAsync(new global::Mfa.DomainService.Configuration.Configuration { EnableMfa = true });

            // impersonated: true (the default) = the context tenant database; false would write the root DB (C5).
            kv.Verify(k => k.SetAsync("mfa-config", It.IsAny<MfaConfiguration>(), true, It.IsAny<CancellationToken>()), Times.Once);
            kv.Verify(k => k.SetAsync(It.IsAny<string>(), It.IsAny<MfaConfiguration>(), false, It.IsAny<CancellationToken>()), Times.Never);
            repo.Verify(r => r.UpsertAsync(It.IsAny<MfaConfiguration>(), It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SaveAsync_KvHit_KeepsTheStoredIdentity_AndUpdatesFields()
        {
            var stored = BuildConfig(enableMfa: false);
            stored.ItemId = "kv-id";
            stored.Name = "Default";
            var kv = Kv(stored);
            MfaConfiguration? saved = null;
            kv.Setup(k => k.SetAsync(It.IsAny<string>(), It.IsAny<MfaConfiguration>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback<string, MfaConfiguration, bool, CancellationToken>((_, c, _, _) => saved = c)
                .Returns(Task.CompletedTask);
            var repo = OldDoc(BuildConfig());

            await new MfaConfigurationService(repo.Object, kv.Object)
                .SaveAsync(new global::Mfa.DomainService.Configuration.Configuration { EnableMfa = true, BackupCodesCount = 7 });

            saved!.ItemId.Should().Be("kv-id");
            saved.EnableMfa.Should().BeTrue();
            saved.BackupCodesCount.Should().Be(7);
            repo.Verify(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SaveAsync_KvMiss_StartsFromTheOldDoc()
        {
            var old = BuildConfig();
            old.ItemId = "old-id";
            old.Name = "Default";
            var kv = Kv();
            MfaConfiguration? saved = null;
            kv.Setup(k => k.SetAsync(It.IsAny<string>(), It.IsAny<MfaConfiguration>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback<string, MfaConfiguration, bool, CancellationToken>((_, c, _, _) => saved = c)
                .Returns(Task.CompletedTask);

            await new MfaConfigurationService(OldDoc(old).Object, kv.Object)
                .SaveAsync(new global::Mfa.DomainService.Configuration.Configuration { EnableMfa = true });

            saved!.ItemId.Should().Be("old-id");
        }

        [Fact]
        public async Task SaveAsync_KvReadThrows_Throws_AndWritesNothing()
        {
            var kv = new Mock<IKeyValueStore>();
            kv.Setup(k => k.GetAsync<MfaConfiguration>(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("kv down"));

            await new MfaConfigurationService(OldDoc(null).Object, kv.Object)
                .Invoking(s => s.SaveAsync(new global::Mfa.DomainService.Configuration.Configuration()))
                .Should().ThrowAsync<InvalidOperationException>();
            kv.Verify(k => k.SetAsync(It.IsAny<string>(), It.IsAny<MfaConfiguration>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
