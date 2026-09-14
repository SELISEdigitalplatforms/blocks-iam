using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public sealed class IdentityConfiguration
    {
        public const int DefaultAccessTokenValidForNumberMinutes = 7;
        public const int DefaultRefreshTokenValidForNumberMinutes = 30;
        public const int DefaultAbsoluteRefreshTokenValidForNumberMinutes = 7 * 60 * 24;
        public const int DefaultRememberMeRefreshTokenValidForNumberMinutes = 30 * 60 * 24;
        public const int DefaultRememberMeAbsoluteRefreshTokenValidForNumberMinutes = 3 * 30 * 60 * 24;
        public const int DefaultGetNumberOfWrongAttemptsToLockTheAccount = 5;
        public const int DefaultAccountLockDurationInMinutes = 15;
        public const int DefaultTokenRotationGracePeriodMinutes = 5;
        public const int DefaultMaxTokenRotationAttempts = 3;
        public const int DefaultActivationUrlLifetimeInMinutes = 60 * 24;
        public const int DefaultRecoverAccountUrlLifetimeInMinutes = 10;
        public const bool DefaultCollectPasswordOnActivation = true;


        [BsonId]
        public ObjectId ItemId { get; set; }
        public List<string> AllowedGrantTypes { get; set; } = new List<string>();
        public int AccessTokenValidForNumberMinutes { get; init; } = DefaultAccessTokenValidForNumberMinutes;
        public int RefreshTokenValidForNumberMinutes { get; set; } = DefaultRefreshTokenValidForNumberMinutes;
        public int AbsoluteRefreshTokenValidForNumberMinutes { get; set; } = DefaultAbsoluteRefreshTokenValidForNumberMinutes;
        public int RememberMeRefreshTokenValidForNumberMinutes { get; init; } = DefaultRememberMeRefreshTokenValidForNumberMinutes;
        public int RememberMeAbsoluteRefreshTokenValidForNumberMinutes { get; init; } = DefaultRememberMeAbsoluteRefreshTokenValidForNumberMinutes;
        public int GetNumberOfWrongAttemptsToLockTheAccount { get; set; } = DefaultGetNumberOfWrongAttemptsToLockTheAccount;
        public int AccountLockDurationInMinutes { get; set; } = DefaultAccountLockDurationInMinutes;
        public int TokenRotationGracePeriodMinutes { get; set; } = DefaultTokenRotationGracePeriodMinutes;
        public int MaxTokenRotationAttempts { get; set; } = DefaultMaxTokenRotationAttempts;

        public string PublicCertificatePath { get; set; }
        public string AccountActivationPath { get; set; }
        public string AccountVerificationPath { get; set; }
        public string RecoverAccountPath { get; set; }
        public bool IsOidcEnabled { get; set; } = false;
        public string AccountActionBaseUrl { get; set; }
        public bool UseAccountActionBaseUrlAsDefault { get; set; } = true;
        public int ActivationUrlLifetimeInMinutes { get; set; } = DefaultActivationUrlLifetimeInMinutes;
        public int RecoverAccountUrlLifetimeInMinutes { get; set; } = DefaultRecoverAccountUrlLifetimeInMinutes;
        public bool LogoutOnPasswordChange { get; set; } = true;
        public string PasswordStrengthCheckerRegex { get; set; }

        /// <summary>
        /// Whether the activation page asks the user to create a password before the account
        /// becomes usable. When false, confirming the emailed link is enough to activate and the
        /// user is sent straight to login. Only the IAM-hosted OIDC activation page consumes
        /// this, so it has no effect while <see cref="IsOidcEnabled"/> is false.
        /// </summary>
        public bool CollectPasswordOnActivation { get; set; } = DefaultCollectPasswordOnActivation;
    }
}