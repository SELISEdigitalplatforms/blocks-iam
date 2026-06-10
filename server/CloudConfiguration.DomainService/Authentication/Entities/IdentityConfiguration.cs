using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CloudConfiguration.DomainService.Authentication.Entities
{
    [BsonIgnoreExtraElements]
    public class IdentityConfiguration
    {
        public const int DefaultAccessTokenValidForNumberMinutes = 7;
        public const int DefaultRefreshTokenValidForNumberMinutes = 30;
        public const int DefaultAbsoluteRefreshTokenValidForNumberMinutes = 7 * 60 * 24;
        public const int DefaultRememberMeRefreshTokenValidForNumberMinutes = 30 * 60 * 24;
        public const int DefaultGetNumberOfWrongAttemptsToLockTheAccount = 5;
        public const int DefaultAccountLockDurationInMinutes = 5;

        [BsonId]
        public ObjectId ItemId { get; set; }
        public List<string> AllowedGrantTypes { get; set; } = [];
        public int AccessTokenValidForNumberMinutes { get; init; } = DefaultAccessTokenValidForNumberMinutes;
        public int RefreshTokenValidForNumberMinutes { get; set; } = DefaultRefreshTokenValidForNumberMinutes;
        public int AbsoluteRefreshTokenValidForNumberMinutes { get; set; } = DefaultAbsoluteRefreshTokenValidForNumberMinutes;
        public int RememberMeRefreshTokenValidForNumberMinutes { get; init; } = DefaultRememberMeRefreshTokenValidForNumberMinutes;
        public int GetNumberOfWrongAttemptsToLockTheAccount { get; set; } = DefaultGetNumberOfWrongAttemptsToLockTheAccount;
        public int AccountLockDurationInMinutes { get; set; } = DefaultAccountLockDurationInMinutes;
        public string PublicCertificatePath { get; set; }

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
