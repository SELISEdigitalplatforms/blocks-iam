using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class UserKeyMap
    {
        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public DateTime IssueDate { get; set; }
        public DateTime ExpireDate { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string MailPurpose { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool Activated { get; set; }
    }
}
