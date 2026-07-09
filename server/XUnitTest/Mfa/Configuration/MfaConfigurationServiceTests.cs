using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Services;
using Moq;

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

        [Fact]
        public async Task GetAsync_WhenNoConfigInRepo_ReturnsDefaultEmptyConfiguration()
        {
            var repo = new Mock<IMfaManagementRepository>();
            repo.Setup(r => r.GetItemAsync<MfaConfiguration>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((MfaConfiguration?)null);

            var service = new MfaConfigurationService(repo.Object);
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

            var service = new MfaConfigurationService(repo.Object);
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

            var service = new MfaConfigurationService(repo.Object);
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

            var service = new MfaConfigurationService(repo.Object);
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
            repo.Setup(r => r.UpsertAsync(It.IsAny<MfaConfiguration>(), It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()))
                .Callback<MfaConfiguration, System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>, string>((c, _, _) => upserted = c)
                .Returns(Task.CompletedTask);

            var service = new MfaConfigurationService(repo.Object);
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
            repo.Setup(r => r.UpsertAsync(It.IsAny<MfaConfiguration>(), It.IsAny<System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>>(), It.IsAny<string>()))
                .Callback<MfaConfiguration, System.Linq.Expressions.Expression<Func<MfaConfiguration, bool>>, string>((c, _, _) => upserted = c)
                .Returns(Task.CompletedTask);

            var service = new MfaConfigurationService(repo.Object);
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
    }
}
