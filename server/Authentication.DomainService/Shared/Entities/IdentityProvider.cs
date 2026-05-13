using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class IdentityProvider : BaseEntity
    {
        public required string Provider { get; set; }
        public required string ProviderType { get; set; }
        public required string Protocol { get; set; }
        public required string DisplayName { get; set; }
        public bool IsActive { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public string? Issuer { get; set; }
        public required string AuthorizationUrl { get; set; }
        public required string TokenUrl { get; set; }
        public string? UserInfoUrl { get; set; }
        public string? JwksUri { get; set; }
        public string? WellKnownUrl { get; set; }
        public string? RedirectUri { get; set; }
        public required string Scope { get; set; }
        public required string ResponseType { get; set; }
        public List<string> GrantTypes { get; set; } = [];
        public bool RequirePkce { get; set; }
        public required string TokenEndpointAuthMethod { get; set; }
        public List<string> InitialRoles { get; set; } = [];
        public List<string> InitialPermissions { get; set; } = [];
        public string? Icon { get; set; }
        
        // Apple-specific properties
        public string? TeamId { get; set; }
        public string? KeyId { get; set; }
        public string? PrivateKey { get; set; }
        public string? AppleAudience { get; set; }
    }
}
