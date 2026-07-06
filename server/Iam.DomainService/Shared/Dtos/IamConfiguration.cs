using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Dtos
{
    [BsonIgnoreExtraElements]
    public class IamConfiguration
    {
        [BsonId]
        public ObjectId ItemId { get; set; }
        public string AccountActivationPath { get; set; } = string.Empty;
        public string AccountVerificationPath { get; set; } = string.Empty;
        public string RecoverAccountPath { get; set; } = string.Empty;
        public bool IsOidcEnabled { get; set; } = false;
        public string AccountActionBaseUrl { get; set; } = string.Empty;
        public bool UseAccountActionBaseUrlAsDefault { get; set; } = true;
        public int ActivationUrlLifetimeInMinutes { get; set; } = 60 * 24;
        public int RecoverAccountUrlLifetimeInMinutes { get; set; } = 10;
        public bool LogoutOnPasswordChange { get; set; } = true;
        public string PasswordStrengthCheckerRegex { get; set; } = string.Empty;
    }
}
