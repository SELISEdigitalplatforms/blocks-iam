using Mfa.DomainService.Services;

namespace Mfa.DomainService.Configuration
{
    public class MfaConfigurationService : IMfaConfigurationService
    {
        private const string DefaultConfigName = "Default";
        private const int DefaultBackupCodesCount = 10;

        private readonly IMfaManagementRepository _repository;

        public MfaConfigurationService(IMfaManagementRepository repository)
        {
            _repository = repository;
        }

        public async Task<Configuration?> GetAsync()
        {
            var repoConfiguration = await _repository.GetItemAsync<MfaConfiguration>(m => m.Name == DefaultConfigName);

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
            var existing = await _repository.GetItemAsync<MfaConfiguration>(m => m.Name == DefaultConfigName);

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

            await _repository.UpsertAsync(existing, m => m.Name == DefaultConfigName);
        }
    }
}
