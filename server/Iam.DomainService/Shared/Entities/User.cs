using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class User : BaseEntity
    {
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }
        public Dictionary<string, List<string>> Roles { get; set; } = new();
        public Dictionary<string, List<string>> Permissions { get; set; } = new();
        public bool Active { get; set; }
        public bool IsVerified { get; set; }
        public UserVerifiedType VerifiedType { get; set; } = UserVerifiedType.None;
        public string? ProfileImageUrl { get; set; }
        public string? ProfileImageId { get; set; }
        public string? Platform { get; set; }
        public UserCreationType UserCreationType { get; set; } = UserCreationType.None;
        public UserProvisioningSource ProvisioningSource { get; set; } = UserProvisioningSource.Manual;
        public UserPassType UserPassType { get; set; } = UserPassType.None;
        public string? Password { get; set; }
        public DateTime PasswordSetTime { get; set; }
        public DateTime? PasswordChangedAtUtc { get; set; }
        public DateTime? LastCredentialRotationAtUtc { get; set; }
        public int FailedLoginCount { get; set; }
        public DateTime? LastFailedLoginUtc { get; set; }
        public int FailedMfaCount { get; set; }
        public DateTime? LastFailedMfaUtc { get; set; }
        public DateTime? LockoutUntilUtc { get; set; }
        public int LockoutCount { get; set; } // Tracks how many times account has been locked (for exponential backoff)
        public DateTime? LastLockoutUtc { get; set; } // When the last lockout was applied
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
        public int TokenVersion { get; set; } = 1;
        public UserMfaType UserMfaType { get; set; } = UserMfaType.None;
        public bool MfaEnabled { get; set; }
        public List<UserMfaEnrollment> MfaMethods { get; set; } = new List<UserMfaEnrollment>();
        public DateTime FirstLoggedInTime { get; set; }
        public DateTime LastLoggedInTime { get; set; }
        public string? LastUsedOrganizationId { get; set; }
        public string LastLoggedInDeviceInfo { get; set; } = string.Empty;
        public int LogInCount { get; set; }
        public List<UserLogInType> AllowedLogInType { get; set; } = new List<UserLogInType>();
        public string? MailPurpose { get; set; }
        public bool IsMfaVerified { get; set; }
        public DateTime? EmailVerifiedAtUtc { get; set; }
        public DateTime? PhoneVerifiedAtUtc { get; set; }
        public DateTime? TermsAcceptedAtUtc { get; set; }
        public DateTime? PrivacyAcceptedAtUtc { get; set; }
        public UserLifecycleStatus Status { get; set; } = UserLifecycleStatus.Active;
        public string? StatusReason { get; set; }
        public DateTime? DeactivatedAtUtc { get; set; }
        public string? DeactivatedBy { get; set; }
        public string? ExternalUserId { get; set; }
        public List<ExternalIdentity> ExternalIdentities { get; set; } = new List<ExternalIdentity>();
        public List<string> OrganizationIds { get; set; } = [];
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>(); // For any additional info that doesn't fit into existing properties
    }

    public class UserMfaEnrollment
    {
        public string Method { get; set; } = string.Empty;
        public DateTime EnrolledAtUtc { get; set; }
        public DateTime? VerifiedAtUtc { get; set; }
        public bool Active { get; set; }
    }

    public class ExternalIdentity
    {
        public string Provider { get; set; } = string.Empty;
        public string ProviderUserId { get; set; } = string.Empty;
        public string? Issuer { get; set; }
        public DateTime LinkedAtUtc { get; set; }
    }

    public enum UserVerifiedType
    {
        None,
        Email,
        Sms, 
        WhatsApp
    }

    public enum UserCreationType
    {
        None,
        Portal,
        Api,
        Service,
        Social,
        ThirdParty,
    }

    public enum UserProvisioningSource
    {
        Manual,
        SCIM,
        Social,
        API
    }

    public enum UserLifecycleStatus
    {
        PendingVerification,
        Active,
        Suspended,
        Disabled
    }

    public enum UserPassType
    {
        None,
        Password,
        Pin
    }

    public enum UserMfaType
    {
        None,
        TOTP,
        Email,
        Sms,
        WhatsApp,
        
    }
    public enum UserLogInType
    {
        None,
        Password,
        SSO,
        AuthrizationCode
    }
}
