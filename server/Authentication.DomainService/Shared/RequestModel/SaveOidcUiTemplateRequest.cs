using Authentication.DomainService.Entities;

namespace Authentication.DomainService.Shared.RequestModel
{
    /// <summary>
    /// Complete tenant OIDC UI template submitted through the protected management API.
    /// </summary>
    public sealed class SaveOidcUiTemplateRequest
    {
        public OidcUiTemplateBranding? Branding { get; set; }
        public OidcUiTemplateTheme? Theme { get; set; }
        public OidcUiTemplatePages? Pages { get; set; }
    }
}
