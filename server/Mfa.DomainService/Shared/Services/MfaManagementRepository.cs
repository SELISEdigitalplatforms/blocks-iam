using Blocks.Genesis;
using Iam.DomainService.Services;
using MongoDB.Driver;
using System.Linq.Expressions;

namespace Mfa.DomainService.Services
{
    public class MfaManagementRepository : BaseRepository, IMfaManagementRepository
    {
        public MfaManagementRepository(IDbContextProvider dbContextProvider, IIamDataAccess dataAccess) : base(dbContextProvider, dataAccess)
        {
        }

        public async Task DeleteItemsAsync<T>(Expression<Func<T, bool>> dataFilters)
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(typeof(T).Name + "s");
            var schema = IamSchemas.For(typeof(T));
            if (schema is null)
            {
                await collection.DeleteManyAsync(dataFilters);
                return;
            }

            var del = await _dataAccess.DeleteFilterAsync(schema, Builders<T>.Filter.Where(dataFilters), "MfaManagementRepository.DeleteItemsAsync");
            if (del is null) return;
            await collection.DeleteManyAsync(del);
        }

        public async Task<IList<T>> GetItemsAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            var collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            var schema = IamSchemas.For(typeof(T));
            if (schema is null) return await collection.Find(filter).ToListAsync();

            return await _dataAccess.FindListAsync(collection, schema, filter, "MfaManagementRepository.GetItemsAsync");
        }

        public async Task<T> GetItemAsync<T>(Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            var collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? typeof(T).Name + "s" : collectionName);
            var filterBuilder = Builders<T>.Filter;
            var filter = filterBuilder.Where(filterExpression);

            var schema = IamSchemas.For(typeof(T));
            if (schema is null) return await collection.Find(filter).FirstOrDefaultAsync();

            return (await _dataAccess.FindFirstAsync(collection, schema, filter, "MfaManagementRepository.GetItemAsync"))!;
        }

        public async Task SaveAsync<T>(T data, string collectionName = "")
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);
            var schema = IamSchemas.For(typeof(T));
            if (schema is not null) await _dataAccess.EnsureInsertAllowedAsync(schema, "MfaManagementRepository.SaveAsync");
            await collection.InsertOneAsync(data);
        }

        public async Task SaveAsync<T>(List<T> listOfData)
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(typeof(T).Name + "s");
            var schema = IamSchemas.For(typeof(T));
            if (schema is not null) await _dataAccess.EnsureInsertAllowedAsync(schema, "MfaManagementRepository.SaveAsync.List");
            await collection.InsertManyAsync(listOfData);
        }

        public async Task UpsertAsync<T>(T data, Expression<Func<T, bool>> filterExpression, string collectionName = "")
        {
            IMongoCollection<T> collection = _dbContextProvider.GetCollection<T>(string.IsNullOrWhiteSpace(collectionName) ? (typeof(T).Name + "s") : collectionName);

            var options = new ReplaceOptions { IsUpsert = true };
            var schema = IamSchemas.For(typeof(T));
            if (schema is null)
            {
                await collection.ReplaceOneAsync(filterExpression, data, options);
                return;
            }

            await _dataAccess.ReplaceOneAsync(collection, schema, Builders<T>.Filter.Where(filterExpression), data, "MfaManagementRepository.UpsertAsync", options);
        }
    }
}
