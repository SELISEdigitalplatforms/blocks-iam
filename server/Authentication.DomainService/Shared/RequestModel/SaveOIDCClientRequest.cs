using Iam.DomainService.Entities;

namespace Authentication.DomainService.RequestModel
{
    public class SaveOIDCClientRequest
    {
        public List<string> RedirectUris { get; set; } = new();
        public List<string> PostLogoutRedirectUris { get; set; } = new();
        public string? Scope { get; set; }
        public List<string> AllowedScopes { get; set; } = new();
        public string? ServiceAccessResource { get; set; }
        public List<string> AllowedServiceAccessResources { get; set; } = new();
        public List<string> AllowedResponseTypes { get; set; } = new() { "code" };
        public bool RequirePkce { get; set; } = true;
        public bool RequireConsent { get; set; }
        public string? FrontChannelLogoutUri { get; set; }
        public string? BackChannelLogoutUri { get; set; }
        public bool IsAutoRedirect { get; set; }
        public string? ExternalDiscoveryEndpoint { get; set; }
        public bool IsActive { get; set; } = true;
        public string? LoginMode { get; set; }
        public string? ClientType { get; set; }
        public string? ItemId { get; set; }
        public string? ClientLogoUrl { get; set; }
        public string? ClientDisplayName { get; set; }
        public string? ClientBrandColor { get; set; }
        public bool UseTokensCookie { get; set; } = true;
        public bool RequireMfa { get; set; }
        public List<UserMfaType>? AllowedMfaMethods { get; set; }
    }
}
