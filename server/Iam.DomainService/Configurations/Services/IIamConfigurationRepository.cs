using Iam.DomainService.Dtos;

namespace Iam.DomainService.Configurations
{
    public interface IIamConfigurationRepository
    {
        Task<bool> SaveConfigurationAsync(IamConfiguration iamConfiguration);
        Task<IamConfiguration> GetConfigurationAsync();
    }
}
