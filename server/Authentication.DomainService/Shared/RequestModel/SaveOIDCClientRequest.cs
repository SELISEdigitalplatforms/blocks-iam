
using Blocks.Genesis;

namespace Authentication.DomainService.RequestModel
{
    public class SaveOIDCClientRequest : IProjectKey
    {
        public string? RedirectUri { get; set; }
        public List<string> RedirectUris { get; set; } = [];
        public List<string> PostLogoutRedirectUris { get; set; } = [];
        public string? Scope { get; set; }
        public List<string> AllowedScopes { get; set; } = [];
        public string? ServiceAccessResource { get; set; }
        public List<string> AllowedServiceAccessResources { get; set; } = [];
        public List<string> AllowedGrantTypes { get; set; } = [];
        public List<string> AllowedResponseTypes { get; set; } = ["code"];
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
        public string? ProjectKey { get; set; }
        public string? ClientLogoUrl { get; set; }
        public string? ClientDisplayName { get; set; }
        public string? ClientBrandColor { get; set; }
        public bool UseTokensCookie { get; set; } = true;
    }
}
