using Authentication.DomainService.Entities;

namespace Authentication.DomainService.Shared.ResponseModel
{
    /// <summary>Public configuration consumed by the OIDC user interface.</summary>
    public sealed class OidcUiConfigResponse
    {
        public OidcUiCaptchaResponse? Captcha { get; set; }
        public OidcUiTemplate? Template { get; set; }
    }

    /// <summary>
    /// The existing public captcha contract. Keep these property names and values stable.
    /// </summary>
    public sealed class OidcUiCaptchaResponse
    {
        public string Key { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Generator { get; set; } = string.Empty;
    }
}
