using Iam.DomainService.Entities;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.Dtos
{
    [BsonIgnoreExtraElements]
    public class GetAccounts
    {
        [BsonId]
        public string ItemId { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastUpdatedDate { get; set; }
        public string? Language { get; set; }
        public string? Salutation { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string Email { get; set; }
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }
        public List<string> OrganizationIds { get; set; } = [];
        public string? LastUsedOrganizationId { get; set; }
        public Dictionary<string, List<string>> Roles { get; set; } = new();
        public Dictionary<string, List<string>> Permissions { get; set; } = new();
        public bool Active { get; set; }
        public UserLifecycleStatus Status { get; set; } = UserLifecycleStatus.Active;
        public bool IsVerified { get; set; }
        public string? ProfileImageUrl { get; set; }
        public bool MfaEnabled { get; set; }
        public bool IsMfaVerified { get; set; }
        public UserMfaType UserMfaType { get; set; }
        public UserProvisioningSource ProvisioningSource { get; set; } = UserProvisioningSource.Manual;
        public List<ExternalIdentity> ExternalIdentities { get; set; } = [];
        public UserCreationType UserCreationType { get; set; }
        public int LogInCount { get; set; }
        public DateTime LastLoggedInTime { get; set; }

        /// <summary>
        /// When the account's lockout expires, or null if it has never been locked out. Read-only
        /// here: this DTO is a projection of User and nothing in the query path writes it.
        /// LockoutCount and LastLockoutUtc are deliberately not projected - no consumer yet.
        /// </summary>
        public DateTime? LockoutUntilUtc { get; set; }
        public string LastLoggedInDeviceInfo { get; set; } = string.Empty;
        public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
    }

}
