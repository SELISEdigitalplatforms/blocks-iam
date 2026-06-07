using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class IdentityProvider : BaseEntity
    {
        public required string Provider { get; set; } // e.g., google, microsoft, apple, okta, auth0, blocks-idp
        public required string ProviderType { get; set; } // social, enterprise, custom, internal
        public string? Protocol { get; set; } = "oidc"; // e.g., oidc, oauth2, saml, ldap
        public string? DisplayName { get; set; }
        public bool IsActive { get; set; }
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }
        public string? Issuer { get; set; }
        public  string? AuthorizationUrl { get; set; }
        public  string? TokenUrl { get; set; }
        public string? UserInfoUrl { get; set; }
        public string? JwksUri { get; set; }
        public string? WellKnownUrl { get; set; }
        public List<string> RedirectUris { get; set; } = [];
        public string? Scope { get; set; }
        public string? ResponseType { get; set; }
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
