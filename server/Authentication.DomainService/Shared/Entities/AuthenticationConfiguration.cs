using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class AuthenticationConfiguration
    {
        public const int DefaultAccessTokenValidForNumberMinutes = 7;
        public const int DefaultRefreshTokenValidForNumberMinutes = 30;
        public const int DefaultAbsoluteRefreshTokenValidForNumberMinutes = 7 * 60 * 24;
        public const int DefaultRememberMeRefreshTokenValidForNumberMinutes = 30 * 60 * 24;
        public const int DefaultGetNumberOfWrongAttemptsToLockTheAccount = 5;
        public const int DefaultAccountLockDurationInMinutes = 5;
        public const int DefaultTokenRotationGracePeriodMinutes = 5;
        public const int DefaultMaxTokenRotationAttempts = 3;
        
        // Exponential backoff: lockout duration in minutes for each lockout count
        public const int DefaultLockoutDuration_1stLockout = 5;        // 5 minutes
        public const int DefaultLockoutDuration_2ndLockout = 15;       // 15 minutes
        public const int DefaultLockoutDuration_3rdLockout = 60;       // 1 hour
        public const int DefaultLockoutDuration_4thPlusLockout = 1440; // 24 hours
        public const int DefaultLockoutCountResetWindowDays = 7;       // Reset counter if no lockouts in 7 days
        
        // IP-based rate limiting
        public const int DefaultMaxLoginAttemptsPerIpPerHour = 100;    // Max attempts from single IP per hour (configurable)
        public const int DefaultMaxLoginAttemptsPerIpPerDay = 500;     // Max attempts from single IP per day (configurable)

        [BsonId]
        public ObjectId ItemId { get; set; }
        public List<string> AllowedGrantTypes { get; set; } = new List<string>();
        public int AccessTokenValidForNumberMinutes { get; init; } = DefaultAccessTokenValidForNumberMinutes;
        public int RefreshTokenValidForNumberMinutes { get; set; } = DefaultRefreshTokenValidForNumberMinutes;
        public int AbsoluteRefreshTokenValidForNumberMinutes { get; set; } = DefaultAbsoluteRefreshTokenValidForNumberMinutes;
        public int RememberMeRefreshTokenValidForNumberMinutes { get; init; } = DefaultRememberMeRefreshTokenValidForNumberMinutes;
        public int GetNumberOfWrongAttemptsToLockTheAccount { get; set; } = DefaultGetNumberOfWrongAttemptsToLockTheAccount;
        public int AccountLockDurationInMinutes { get; set; } = DefaultAccountLockDurationInMinutes;
        public int TokenRotationGracePeriodMinutes { get; set; } = DefaultTokenRotationGracePeriodMinutes;
        public int MaxTokenRotationAttempts { get; set; } = DefaultMaxTokenRotationAttempts;
        
        // Exponential backoff settings
        public int LockoutDuration_1stLockout { get; set; } = DefaultLockoutDuration_1stLockout;
        public int LockoutDuration_2ndLockout { get; set; } = DefaultLockoutDuration_2ndLockout;
        public int LockoutDuration_3rdLockout { get; set; } = DefaultLockoutDuration_3rdLockout;
        public int LockoutDuration_4thPlusLockout { get; set; } = DefaultLockoutDuration_4thPlusLockout;
        public int LockoutCountResetWindowDays { get; set; } = DefaultLockoutCountResetWindowDays;
        
        // IP-based rate limiting
        public int MaxLoginAttemptsPerIpPerHour { get; set; } = DefaultMaxLoginAttemptsPerIpPerHour;
        public int MaxLoginAttemptsPerIpPerDay { get; set; } = DefaultMaxLoginAttemptsPerIpPerDay;
    }
}
