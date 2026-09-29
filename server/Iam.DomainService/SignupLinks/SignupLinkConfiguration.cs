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
    /// <summary>OIDC mode only; empty for an embedded configuration.</summary>
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? DefaultForwardedTo { get; set; }
    public SignupLinkCredentialMode CredentialMode { get; set; }

    /// <summary>Oidc for every document written before this field existed. See SignupLinkMode.</summary>
    public SignupLinkMode Mode { get; set; }

    /// <summary>
    /// Embedded mode only. Used ONLY to compose the url string returned to the caller at
    /// generation; the server never navigates to it. A URL handed back to the party that
    /// already holds the one-time code grants nothing, while the same string used as a
    /// Location header would be an open redirect.
    /// </summary>
    public string? JoinUrl { get; set; }
    public int DefaultLifetimeMinutes { get; set; } = 1440;
    public int? DefaultMaxRedemptions { get; set; }
    public bool IsActive { get; set; } = true;
}
