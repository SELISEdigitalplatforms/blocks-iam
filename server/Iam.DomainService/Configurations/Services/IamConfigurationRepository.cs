using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using MongoDB.Driver;

namespace Iam.DomainService.Configurations
{
    public class IamConfigurationRepository : IIamConfigurationRepository
    {
        private const string IdentityConfigurationCollectionName = "IdentityConfigurations";
        private readonly IIdentityAccessManagementRepository _identityAccessManagementRepository;
        private readonly IIamDataAccess _dataAccess;

        public IamConfigurationRepository(IIdentityAccessManagementRepository identityAccessManagementRepository, IIamDataAccess dataAccess)
        {
            _identityAccessManagementRepository = identityAccessManagementRepository;
            _dataAccess = dataAccess;
        }

        public async Task<IamConfiguration> GetConfigurationAsync()
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<IamConfiguration>(IdentityConfigurationCollectionName);
            return await _dataAccess.FindFirstAsync(collection, IamSchemas.IdentityConfiguration, Builders<IamConfiguration>.Filter.Empty, "IamConfigurationRepository.GetConfigurationAsync");
        }
    }
}
