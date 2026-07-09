using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class UserTimeline : BaseTimeline<User>
    {
        public required string UserId { get; set; }
    }
}
