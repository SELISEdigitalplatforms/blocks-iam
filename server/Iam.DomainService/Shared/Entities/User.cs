using Blocks.Genesis;
using Iam.DomainService.Shared.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class User
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
        public DateTime? ActivatedAtUtc { get; set; }
        public string? ActivatedBy { get; set; }
        public string? ExternalUserId { get; set; }
        public List<ExternalIdentity> ExternalIdentities { get; set; } = new List<ExternalIdentity>();
        public List<string> OrganizationIds { get; set; } = [];
        [BsonSerializer(typeof(AttributeBagSerializer))]
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>(); // For any additional info that doesn't fit into existing properties

        #region BaseEntity

        [BsonId]
        public string ItemId { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdatedDate { get; set; }
        public string? CreatedBy { get; set; }
        public string? Language { get; set; }
        public string? LastUpdatedBy { get; set; }
        public List<string> Tags { get; set; } = new List<string>();

        #endregion
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

    /// <summary>
    /// Channel that was used to verify the user's identity (email, phone, etc.).
    /// <see cref="None"/> indicates the user has not completed any verification
    /// step yet.
    /// </summary>
    public enum UserVerifiedType
    {
        /// <summary>No verification channel has been completed.</summary>
        None,

        /// <summary>Identity verified via email link or one-time code.</summary>
        Email,

        /// <summary>Identity verified via SMS one-time code.</summary>
        Sms,

        /// <summary>Identity verified via WhatsApp one-time code.</summary>
        WhatsApp
    }

    /// <summary>
    /// Origin that initiated creation of the user record. Used for audit and
    /// to drive UI badges showing where the account came from.
    /// </summary>
    public enum UserCreationType
    {
        /// <summary>Unknown / unset creation source (legacy records).</summary>
        None,

        /// <summary>Created by an administrator through the management portal.</summary>
        Portal,

        /// <summary>Created programmatically through a public or partner API.</summary>
        Api,

        /// <summary>Created by a trusted internal service or background job.</summary>
        Service,

        /// <summary>Created through a social identity provider (Google, GitHub, etc.).</summary>
        Social,

        /// <summary>Created through a third-party integration (Zapier, partner connector, etc.).</summary>
        ThirdParty,
    }

    /// <summary>
    /// System that actually provisioned the user. Distinct from
    /// <see cref="UserCreationType"/>: a user created via API can still be
    /// provisioned through SCIM if the API is the SCIM endpoint.
    /// </summary>
    public enum UserProvisioningSource
    {
        /// <summary>Created by hand by an administrator.</summary>
        Manual,

        /// <summary>Provisioned through SCIM from an external IdP / HR system.</summary>
        SCIM,

        /// <summary>Provisioned through a social identity provider.</summary>
        Social,

        /// <summary>Provisioned by an authenticated caller through a public or partner API.</summary>
        API
    }

    /// <summary>
    /// Lifecycle status of the user account. Drives login eligibility, UI
    /// visibility, and email/SMS notifications.
    /// </summary>
    public enum UserLifecycleStatus
    {
        /// <summary>Account was created but the user has not completed verification yet.</summary>
        PendingVerification,

        /// <summary>Account is in good standing and can sign in.</summary>
        Active,

        /// <summary>Account is temporarily blocked (e.g. policy violation, suspicious activity) but can be reinstated.</summary>
        Suspended,

        /// <summary>Account is permanently disabled and cannot be reactivated without admin intervention.</summary>
        Disabled
    }

    /// <summary>
    /// Password-type credential the user is enrolled in. Only one value should
    /// be active at a time; switching from <see cref="Password"/> to
    /// <see cref="Pin"/> rotates the stored credential.
    /// </summary>
    public enum UserPassType
    {
        /// <summary>No password credential is set.</summary>
        None,

        /// <summary>Conventional password (hashed with bcrypt).</summary>
        Password,

        /// <summary>Short numeric PIN, typically used for kiosk / mobile unlock flows.</summary>
        Pin
    }

    /// <summary>
    /// Primary multi-factor authentication method the user is enrolled in.
    /// Additional factors may be enrolled via <c>User.MfaMethods</c>.
    /// </summary>
    public enum UserMfaType
    {
        /// <summary>No MFA method is enrolled.</summary>
        None,

        /// <summary>Time-based One-Time Password (RFC 6238) via an authenticator app.</summary>
        TOTP,

        /// <summary>One-time code delivered by email.</summary>
        Email,

        /// <summary>One-time code delivered by SMS.</summary>
        Sms,

        /// <summary>One-time code delivered by WhatsApp.</summary>
        WhatsApp,

    }

    /// <summary>
    /// Login flows the user is allowed to complete. Stored as a list on the
    /// user record so administrators can disable specific channels per user.
    /// </summary>
    public enum UserLogInType
    {
        /// <summary>No login flow is allowed (placeholder / sentinel value).</summary>
        None,

        /// <summary>Standard password-based login.</summary>
        Password,

        /// <summary>Single Sign-On via a configured identity provider.</summary>
        SSO,

        /// <summary>OAuth 2.0 Authorization Code flow (PKCE).</summary>
        AuthrizationCode
    }
}
