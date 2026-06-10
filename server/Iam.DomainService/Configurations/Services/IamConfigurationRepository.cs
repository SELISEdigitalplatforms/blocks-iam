using Iam.DomainService.Dtos;
using Iam.DomainService.Services;
using MongoDB.Driver;

namespace Iam.DomainService.Configurations
{
    public class IamConfigurationRepository : IIamConfigurationRepository
    {
        private const string IdentityConfigurationCollectionName = "IdentityConfigurations";
        private readonly IIdentityAccessManagementRepository _identityAccessManagementRepository;

        public IamConfigurationRepository(IIdentityAccessManagementRepository identityAccessManagementRepository)
        {
            _identityAccessManagementRepository = identityAccessManagementRepository;
        }

        public async Task<IamConfiguration> GetConfigurationAsync()
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<IamConfiguration>(IdentityConfigurationCollectionName);
            return await collection.Find(_ => true).FirstOrDefaultAsync();
        }

        public async Task<bool> SaveConfigurationAsync(IamConfiguration iamConfiguration)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<IamConfiguration>(IdentityConfigurationCollectionName);

            var filter = Builders<IamConfiguration>.Filter.Eq("_id", iamConfiguration.ItemId);

            var result = await collection.ReplaceOneAsync(
                filter,
                iamConfiguration,
                new ReplaceOptions { IsUpsert = true });

            return result.IsAcknowledged;
        }
    }
}
