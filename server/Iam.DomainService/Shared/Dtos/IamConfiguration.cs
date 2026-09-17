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

        /// <summary>Short plain-text explanation of the password rule. Empty means no message written.</summary>
        public string PasswordStrengthCheckerMessage { get; set; } = string.Empty;

        /// <summary>
        /// Mirrors IdentityConfiguration's structured PasswordPolicy* fields -- both types are
        /// views over the same configuration document. When true, this is the sole authority
        /// for password strength enforcement; any stored PasswordStrengthCheckerRegex is not
        /// additionally applied.
        /// </summary>
        public int PasswordPolicyMinLength { get; set; } = 8;
        public int PasswordPolicyMaxLength { get; set; } = 64;
        public bool PasswordPolicyRequireUppercase { get; set; } = false;
        public bool PasswordPolicyRequireLowercase { get; set; } = false;
        public bool PasswordPolicyRequireNumbers { get; set; } = false;
        public bool PasswordPolicyRequireSpecialChars { get; set; } = false;

        /// <summary>Short admin-written message the structured flags don't capture. Empty means unset.</summary>
        public string PasswordPolicyMessage { get; set; } = string.Empty;

        /// <summary>
        /// Mirrors IdentityConfiguration.CollectPasswordOnActivation. Both types are views over
        /// the same configuration document, so the flag is kept readable from this side too.
        /// </summary>
        public bool CollectPasswordOnActivation { get; set; } = true;
    }
}
