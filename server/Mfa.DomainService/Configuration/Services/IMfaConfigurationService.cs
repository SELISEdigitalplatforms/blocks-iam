using Blocks.Genesis;

namespace Mfa.DomainService.Configuration
{
    public interface IMfaConfigurationService
    {
        Task<Configuration?> GetAsync();
        Task SaveAsync(Configuration configuration);
    }
}
