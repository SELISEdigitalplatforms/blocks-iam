using MongoDB.Bson.Serialization.Attributes;

namespace Idp.DomainService.Oidc.Contracts;

[BsonIgnoreExtraElements]
public sealed class LinkSessionModel
{
    [BsonId]
    public string SessionId { get; set; } = Guid.NewGuid().ToString("n");
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string LinkId { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public DateTime IssuedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public bool IsExpired(DateTime? nowUtc = null) =>
        ExpiresAtUtc <= (nowUtc ?? DateTime.UtcNow);
}
