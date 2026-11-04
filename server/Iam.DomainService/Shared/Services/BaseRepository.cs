using Blocks.Genesis;
using MongoDB.Driver;

namespace Iam.DomainService.Services
{
    public abstract class BaseRepository
    {
        protected readonly IDbContextProvider _dbContextProvider;
        protected readonly IIamDataAccess _dataAccess;

        protected BaseRepository(IDbContextProvider dbContextProvider, IIamDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _dataAccess = dataAccess;
        }

        public IMongoCollection<T> GetCollection<T>()
        {
            return _dbContextProvider.GetCollection<T>($"{typeof(T).Name}s");
        }

        public IMongoCollection<T> GetCollection<T>(string tenantId)
        {
            return _dbContextProvider.GetCollection<T>(tenantId, $"{typeof(T).Name}s");
        }

        public IMongoCollection<T> GetCollectionByName<T>(string collectionName)
        {
            return _dbContextProvider.GetCollection<T>(collectionName);
        }

        public async Task UpdatePartialAsync<T>(string id, Dictionary<string, object> updates, string collectionName = "")
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);

            FilterDefinition<T> filter = Builders<T>.Filter.Eq("_id", id);
            var updateDefinition = new List<UpdateDefinition<T>>();

            foreach (var update in updates)
            {
                updateDefinition.Add(Builders<T>.Update.Set(update.Key, update.Value));
            }

            var combinedUpdate = Builders<T>.Update.Combine(updateDefinition);
            var schema = IamSchemas.For(typeof(T));
            if (schema is not null)
            {
                var edit = await _dataAccess.EditFilterAsync(schema, filter, "BaseRepository.UpdatePartialAsync");
                if (edit is null) return;
                await _dataAccess.CheckUpdateAsync(schema, combinedUpdate, "BaseRepository.UpdatePartialAsync");
                filter = edit;
            }
            await collection.UpdateOneAsync(filter, combinedUpdate);
        }
    }
}
