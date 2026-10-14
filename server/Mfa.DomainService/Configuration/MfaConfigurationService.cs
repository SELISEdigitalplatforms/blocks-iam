using Blocks.Genesis;
using Mfa.DomainService.Services;
using Blocks.Genesis.Entities.Iam;

namespace Mfa.DomainService.Configuration
{
    public class MfaConfigurationService : IMfaConfigurationService
    {
        private const string DefaultConfigName = "Default";
        private const int DefaultBackupCodesCount = 10;

        /// <summary>The key of the MFA policy row in the tenant database's <c>KeyValueStores</c>.</summary>
        public const string StoreKey = "mfa-config";

        private readonly IMfaManagementRepository _repository;
        private readonly IKeyValueStore _keyValueStore;

        public MfaConfigurationService(IMfaManagementRepository repository, IKeyValueStore keyValueStore)
        {
            _repository = repository;
            _keyValueStore = keyValueStore;
        }

        public async Task<Configuration?> GetAsync()
        {
            // A read error throws, as it always did. It never turns into "MFA off".
            var repoConfiguration = await _keyValueStore.GetAsync<MfaConfiguration>(StoreKey)
                // Not saved to the store yet (before the migration): the old document.
                ?? await _repository.GetItemAsync<MfaConfiguration>(m => m.Name == DefaultConfigName);

            if (repoConfiguration == null)
            {
                return new Configuration
                {
                    MfaTemplate = new MfaTemplate(),
                    UserMfaType = [],
                    EnableMfa = false
                };
            }

            return new Configuration
            {
                EnableMfa = repoConfiguration.EnableMfa,
                UserMfaType = repoConfiguration.UserMfaTypes ?? [],
                MfaTemplate = repoConfiguration.MfaTemplate,
                RequireMfaForAllUsers = repoConfiguration.RequireMfaForAllUsers,
                MfaRequiredRoles = repoConfiguration.MfaRequiredRoles ?? [],
                MfaExemptRoles = repoConfiguration.MfaExemptRoles ?? [],
                AllowUserOptOut = repoConfiguration.AllowUserOptOut,
                AllowBackupCodes = repoConfiguration.AllowBackupCodes,
                BackupCodesCount = repoConfiguration.BackupCodesCount > 0 ? repoConfiguration.BackupCodesCount : DefaultBackupCodesCount
            };
        }

        public async Task SaveAsync(Configuration configuration)
        {
            var existing = await _keyValueStore.GetAsync<MfaConfiguration>(StoreKey)
                ?? await _repository.GetItemAsync<MfaConfiguration>(m => m.Name == DefaultConfigName);

            if (existing == null)
            {
                existing = new MfaConfiguration
                {
                    ItemId = Guid.NewGuid().ToString("n"),
                    Name = DefaultConfigName,
                    CreatedDate = DateTime.UtcNow,
                    LastUpdatedDate = DateTime.UtcNow
                };
            }

            existing.EnableMfa = configuration.EnableMfa;
            existing.UserMfaTypes = configuration.UserMfaType ?? [];
            existing.MfaTemplate = configuration.MfaTemplate ?? new MfaTemplate();
            existing.RequireMfaForAllUsers = configuration.RequireMfaForAllUsers;
            existing.MfaRequiredRoles = configuration.MfaRequiredRoles ?? [];
            existing.MfaExemptRoles = configuration.MfaExemptRoles ?? [];
            existing.AllowUserOptOut = configuration.AllowUserOptOut;
            existing.AllowBackupCodes = configuration.AllowBackupCodes;
            existing.BackupCodesCount = configuration.BackupCodesCount;
            existing.LastUpdatedDate = DateTime.UtcNow;

            // The tenant database of the context (never the root database), and never the old collection.
            await _keyValueStore.SetAsync(StoreKey, existing);
        }
    }
}
