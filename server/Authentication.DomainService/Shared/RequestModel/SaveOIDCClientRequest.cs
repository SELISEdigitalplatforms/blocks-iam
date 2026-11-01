using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;
using Authentication.DomainService.Entities;

namespace Authentication.DomainService.RequestModel
{
    public sealed class SaveOIDCClientRequest
    {
        public List<string> RedirectUris { get; set; } = new();
        public List<string> PostLogoutRedirectUris { get; set; } = new();
        public string? Scope { get; set; }
        public List<string> AllowedScopes { get; set; } = new();
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
        public string? ClientDisplayName { get; set; }
        public bool UseTokensCookie { get; set; } = true;
        public bool RequireMfa { get; set; }
        public List<UserMfaType>? AllowedMfaMethods { get; set; }
        public bool RegisterAsIdentityProvider { get; set; }
        public bool IsDeviceFlowClient { get; set; }

        /// <summary>
        /// Optional per-client token lifetimes. Null or absent keeps the stored values of an existing
        /// client (a new client gets none). When present it replaces them; a null field means "use the
        /// project value" and all-null fields remove the client's values.
        /// </summary>
        public OidcClientTokenLifetimes? TokenLifetimes { get; set; }

        /// <summary>How this client narrows the tenant's sign-in policy. Null keeps what is stored.</summary>
        public OidcClientLoginPolicy? LoginPolicy { get; set; }
    }
}
