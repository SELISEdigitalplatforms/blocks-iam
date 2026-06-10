using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Dtos
{
    [BsonIgnoreExtraElements]
    public class IamConfiguration
    {
        [BsonId]
        public ObjectId ItemId { get; set; }
        public string AccountActivationPath { get; set; }
        public string AccountVerificationPath { get; set; }
        public string RecoverAccountPath { get; set; }
        public bool IsOidcEnabled { get; set; } = false;
        public string AccountActionBaseUrl { get; set; }
        public int ActivationUrlLifetimeInMinutes { get; set; } = 60 * 24;
        public int RecoverAccountUrlLifetimeInMinutes { get; set; } = 10;
        public bool LogoutOnPasswordChange { get; set; } = true;
        public string PasswordStrengthCheckerRegex { get; set; }
    }
}
