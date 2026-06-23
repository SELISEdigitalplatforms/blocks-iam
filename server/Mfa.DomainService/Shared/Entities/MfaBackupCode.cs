using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Mfa.DomainService.Shared
{
    [BsonIgnoreExtraElements]
    public class MfaBackupCode : BaseEntity
    {
        [BsonId]
        public new string ItemId { get; set; } = Guid.NewGuid().ToString("n");
        public string UserId { get; set; } = string.Empty;
        public string CodeHash { get; set; } = string.Empty;
        public string CodePrefix { get; set; } = string.Empty;
        public bool IsUsed { get; set; }
        public DateTime? UsedAtUtc { get; set; }
        public string? UsedFromIp { get; set; }
    }
}
