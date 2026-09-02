using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Migrations
{
    /// <summary>
    /// Minimal snapshot of the branding fields formerly stored on an OIDC client.
    /// Property names deliberately differ from the retired public/domain contract.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class LegacyOidcClientBranding
    {
        [BsonElement("ClientId")]
        public string ClientId { get; init; } = string.Empty;

        [BsonElement("IsActive")]
        public bool IsActive { get; init; } = true;

        [BsonElement("LogoUri")]
        public string? LegacyLogoUrl { get; init; }

        [BsonElement("UiBrandColor")]
        public string? LegacyBrandColor { get; init; }
    }
}
