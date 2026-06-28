using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Mfa.DomainService.TOTP
{
    [BsonIgnoreExtraElements]
    public class UserTotpDetail : BaseEntity
    {
        public string ImageUri { get; set; }
        public string TowFactorId { get; set; }
        public string Secret { get; set; }
    }
}
