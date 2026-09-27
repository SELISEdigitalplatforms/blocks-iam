using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Iam.DomainService.SignupLinks;

[BsonIgnoreExtraElements]
public class SignupLinkConfiguration : BaseEntity
{
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> DefaultRoles { get; set; } = new();
    public List<string> DefaultPermissions { get; set; } = new();
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? DefaultForwardedTo { get; set; }
    public SignupLinkCredentialMode CredentialMode { get; set; }
    public int DefaultLifetimeMinutes { get; set; } = 1440;
    public int? DefaultMaxRedemptions { get; set; }
    public bool IsActive { get; set; } = true;
}
