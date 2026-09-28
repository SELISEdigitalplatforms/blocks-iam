using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.SignupLinks;

[BsonIgnoreExtraElements]
public class SignupLink : BaseEntity
{
    public string CodeHash { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ConfigurationId { get; set; } = string.Empty;
    // OrganizationId inherited from BaseEntity — always concrete at generation
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? ForwardedTo { get; set; }
    public SignupLinkCredentialMode CredentialMode { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    // Language inherited from BaseEntity
    public DateTime ExpiresAtUtc { get; set; }
    public int MaxRedemptions { get; set; } = 1;
    public int RedemptionCount { get; set; }
    public SignupLinkStatus Status { get; set; } = SignupLinkStatus.Active;
    public string? CreatedUserId { get; set; }
    public string? CreatedByClientId { get; set; }
    public string? CreatedByOrganizationId { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedBy { get; set; }
}
