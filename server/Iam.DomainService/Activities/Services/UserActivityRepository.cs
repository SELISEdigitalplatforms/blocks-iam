
using Blocks.Genesis;
using Iam.DomainService.Services;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Iam.DomainService.Activities
{
    public class UserActivityRepository : IUserActivityRepository
    {
        private readonly IIdentityAccessManagementRepository _identityAccessManagementRepository;
        public UserActivityRepository(IIdentityAccessManagementRepository identityAccessManagementRepository)
        {
            _identityAccessManagementRepository = identityAccessManagementRepository;
        }
        public async Task<(IQueryable<object>, long)> GetActiveSessionByUserIdDevicesAsync(BaseActivityRequest request)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<BsonDocument>("Sessions");
            var userId = ResolveUserId(request.Filter?.UserId);
            var filter = Builders<BsonDocument>.Filter.Eq("UserId", userId);
            var sort = Builders<BsonDocument>.Sort.Descending("CreateDate");
            var activeTokens = await collection.Find(filter).Sort(sort).Skip(request.Page * request.PageSize).Limit(request.PageSize).ToListAsync();
            var count = await collection.CountDocumentsAsync(filter);

            return (activeTokens.Select(x => x.ToJson()).AsQueryable(), count);

        }

        public async Task<(IQueryable<object>, long)> GetHistorysByUserIdDevicesAsync(BaseActivityRequest request)
        {
            var collection = _identityAccessManagementRepository.GetCollectionByName<BsonDocument>("UserAuthenticationTimelines");
            var userId = ResolveUserId(request.Filter?.UserId);
            var filter = Builders<BsonDocument>.Filter.Eq("CreatedBy", userId);
            var sort = Builders<BsonDocument>.Sort.Descending("CreatedDate");
            var histories = await collection.Find(filter).Sort(sort).Skip(request.Page * request.PageSize).Limit(request.PageSize).ToListAsync();
            var count = await collection.CountDocumentsAsync(filter);

            return (histories.Select(x => x.ToJson()).AsQueryable(), count);
        }

        private static string ResolveUserId(string? requestedUserId)
        {
            if (!string.IsNullOrWhiteSpace(requestedUserId))
            {
                return requestedUserId;
            }

            return BlocksContext.GetContext()?.UserId ?? string.Empty;
        }
    }
}
