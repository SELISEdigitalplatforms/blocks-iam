using Blocks.Genesis;
using Cloud.DomainService.Models;
using Cloud.DomainService.Requests;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Cloud.DomainService.Repositories
{
    public class ApiEndpointConfigRepository : IApiEndpointConfigRepository
    {
        private const string CollectionName = "ApiEndpointConfigs";

        private readonly IDbContextProvider _dbContextProvider;
        private readonly IBlocksSecret _blocksSecret;

        public ApiEndpointConfigRepository(IDbContextProvider dbContextProvider, IBlocksSecret blocksSecret)
        {
            _dbContextProvider = dbContextProvider;
            _blocksSecret = blocksSecret;
        }

        public async Task<(List<ApiEndpointConfig>, long)> GetListAsync(GetApiEndpointConfigsRequest request)
        {
            var db = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, request.ProjectKey);
            var collection = db.GetCollection<ApiEndpointConfig>(CollectionName);

            var filter = Builders<ApiEndpointConfig>.Filter.Empty;

            if (!string.IsNullOrWhiteSpace(request.Filter?.Service))
                filter &= Builders<ApiEndpointConfig>.Filter.Eq(x => x.Service, request.Filter.Service);

            if (!string.IsNullOrWhiteSpace(request.Filter?.Method))
                filter &= Builders<ApiEndpointConfig>.Filter.Eq(x => x.Method, request.Filter.Method);

            if (!string.IsNullOrWhiteSpace(request.Filter?.Endpoint))
                filter &= Builders<ApiEndpointConfig>.Filter.Regex(x => x.Endpoint, new BsonRegularExpression(request.Filter.Endpoint, "i"));

            var data = await collection.Find(filter)
                .Skip(request.Page * request.PageSize)
                .Limit(request.PageSize)
                .ToListAsync();

            var count = await collection.CountDocumentsAsync(filter);

            return (data, count);
        }

        public async Task<bool> UpdateAsync(string projectKey, string itemId, bool isCaptchaRequired, bool isMfaRequired, string updatedBy)
        {
            var db = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, projectKey);
            var collection = db.GetCollection<ApiEndpointConfig>(CollectionName);

            var filter = Builders<ApiEndpointConfig>.Filter.Eq(x => x.ItemId, itemId);
            var update = Builders<ApiEndpointConfig>.Update
                .Set(x => x.IsCaptchaRequired, isCaptchaRequired)
                .Set(x => x.IsMfaRequired, isMfaRequired)
                .Set(x => x.LastUpdatedBy, updatedBy)
                .Set(x => x.LastUpdatedDate, DateTime.UtcNow);
            var result = await collection.UpdateOneAsync(filter, update);

            return result.ModifiedCount > 0;
        }

        public async Task<long> BulkUpdateAsync(string projectKey, List<string> itemIds, bool isCaptchaRequired, bool isMfaRequired, string updatedBy)
        {
            var db = _dbContextProvider.GetDatabase(_blocksSecret.DatabaseConnectionString, projectKey);
            var collection = db.GetCollection<ApiEndpointConfig>(CollectionName);

            var filter = Builders<ApiEndpointConfig>.Filter.In(x => x.ItemId, itemIds);
            var update = Builders<ApiEndpointConfig>.Update
                .Set(x => x.IsCaptchaRequired, isCaptchaRequired)
                .Set(x => x.IsMfaRequired, isMfaRequired)
                .Set(x => x.LastUpdatedBy, updatedBy)
                .Set(x => x.LastUpdatedDate, DateTime.UtcNow);

            var result = await collection.UpdateManyAsync(filter, update);
            return result.ModifiedCount;
        }
    }
}
