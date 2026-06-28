using Blocks.Genesis;
using MongoDB.Driver;
using CloudConfiguration.DomainService.Authentication.Entities;
using System.Linq.Expressions;

namespace CloudConfiguration.DomainService.Shared.Services
{
    public class ConfigurationRepository : IConfigurationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        private const string _identityConfigurationCollectionName = "IdentityConfigurations";

        public ConfigurationRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        #region Authentication

        public async Task<IdentityConfiguration> GetAuthenticationConfigurationAsync()
        {
            var collection = _dbContextProvider.GetCollection<IdentityConfiguration>(_identityConfigurationCollectionName);
            var filter = Builders<IdentityConfiguration>.Filter.Where(_ => true);
            return await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
        }

        public async Task UpdateAuthenticationConfigAsync(IdentityConfiguration configuration)
        {
            var collection = _dbContextProvider.GetCollection<IdentityConfiguration>(_identityConfigurationCollectionName);
            var filter = Builders<IdentityConfiguration>.Filter.Eq("_id", configuration.ItemId);
            await collection.ReplaceOneAsync(filter, configuration);
        }

        #endregion

        public async Task UpsertAsync<T>(T data, Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);

            var options = new ReplaceOptions { IsUpsert = true };
            await collection.ReplaceOneAsync(filterExpression, data, options);
        }
    }
}