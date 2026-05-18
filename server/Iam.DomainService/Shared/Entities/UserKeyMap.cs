using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class UserKeyMap
    {
        [BsonId]
        public string ItemId { get; set; }
        public string Key { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime ExpireDate { get; set; }
        public string UserId { get; set; }
        public string MailPurpose { get; set; }
        public string Value { get; set; }
        public bool Activated { get; set; }
    }
}
