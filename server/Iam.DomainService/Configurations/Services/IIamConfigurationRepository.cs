using Iam.DomainService.Dtos;

namespace Iam.DomainService.Configurations
{
    public interface IIamConfigurationRepository
    {
        Task<IamConfiguration> GetConfigurationAsync();
    }
}
