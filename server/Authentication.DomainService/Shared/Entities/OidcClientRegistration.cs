using Blocks.Genesis;
using Iam.DomainService.Entities;
using MongoDB.Bson.Serialization.Attributes;

namespace Authentication.DomainService.Entities
{
    [BsonIgnoreExtraElements]
    public class OidcClientRegistration : BaseEntity
    {
        public string ClientId { get; set; } = string.Empty;
        public string? ClientSecret { get; set; }
        public List<string> RedirectUris { get; set; } = new();
        public List<string> PostLogoutRedirectUris { get; set; } = new();
        public List<string> AllowedScopes { get; set; } = new();
        public List<string> AllowedResponseTypes { get; set; } = new() { "code" };
        public string? ClientName { get; set; }
        public string? LogoUri { get; set; }
        public string? TokenEndpointAuthMethod { get; set; }
        public bool RequirePkce { get; set; } = true;
        public bool RequireConsent { get; set; }
        public string? FrontChannelLogoutUri { get; set; }
        public string? BackChannelLogoutUri { get; set; }
        public bool IsAutoRedirect { get; set; }
        public string? ExternalDiscoveryEndpoint { get; set; }
        public bool IsActive { get; set; } = true;
        public string? LoginMode { get; set; }
        public string? ClientType { get; set; }
        public string? UiBrandColor { get; set; }
        public bool UseTokensCookie { get; set; } = true; // Default: tokens in cookies
        public bool RequireMfa { get; set; }
        public List<UserMfaType>? AllowedMfaMethods { get; set; }
        public bool IsDeviceFlowClient { get; set; } = false;

        //[BsonIgnore]
        //public string? RedirectUri
        //{
        //    get => RedirectUris.FirstOrDefault();
        //    set => RedirectUris = string.IsNullOrWhiteSpace(value) ? new() : new() { value };
        //}

        [BsonIgnore]
        public string? Scope
        {
            get => AllowedScopes.Count == 0 ? null : string.Join(' ', AllowedScopes);
            set => AllowedScopes = string.IsNullOrWhiteSpace(value)
                ? new()
                : value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        [BsonIgnore]
        public string? ClientDisplayName
        {
            get => ClientName;
            set => ClientName = value;
        }

        [BsonIgnore]
        public string? ClientLogoUrl
        {
            get => LogoUri;
            set => LogoUri = value;
        }

        [BsonIgnore]
        public string? ClientBrandColor
        {
            get => UiBrandColor;
            set => UiBrandColor = value;
        }

        public bool RegisterAsIdentityProvider { get; set; }

    }
}
