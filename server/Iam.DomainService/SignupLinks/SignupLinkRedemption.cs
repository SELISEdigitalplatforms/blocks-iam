using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.SignupLinks;

[BsonIgnoreExtraElements]
public class SignupLinkRedemption : BaseEntity
{
    public string LinkId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public SignupLinkRedemptionOutcome Outcome { get; set; }
    public string? RejectionReason { get; set; }
    public int RedemptionOrdinal { get; set; }
    public DateTime RedeemedAtUtc { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? GrantedOrganizationId { get; set; }
    public List<string> GrantedRoles { get; set; } = new();
    public List<string> GrantedPermissions { get; set; } = new();
}
