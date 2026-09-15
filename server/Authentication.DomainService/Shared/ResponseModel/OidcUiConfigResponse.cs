using Authentication.DomainService.Entities;

namespace Authentication.DomainService.Shared.ResponseModel
{
    /// <summary>Public configuration consumed by the OIDC user interface.</summary>
    public sealed class OidcUiConfigResponse
    {
        public OidcUiPasswordPolicyResponse? PasswordPolicy { get; set; }

        public OidcUiCaptchaResponse? Captcha { get; set; }
        public OidcUiTemplate? Template { get; set; }

        /// <summary>
        /// Whether the activation page must collect a password. False lets the emailed
        /// confirmation activate the account on its own.
        /// </summary>
        public bool CollectPasswordOnActivation { get; set; } = true;
    }

    /// <summary>
    /// Structured password rule as plain data -- never a regex. See SPEC16: this supersedes the
    /// earlier regex-on-the-wire shape entirely; no property on this type is, or encodes, a
    /// regular expression.
    /// </summary>
    public sealed class OidcUiPasswordPolicyResponse
    {
        public int MinLength { get; set; }
        public int MaxLength { get; set; }
        public bool RequireUppercase { get; set; }
        public bool RequireLowercase { get; set; }
        public bool RequireNumbers { get; set; }
        public bool RequireSpecialChars { get; set; }
        public string? Message { get; set; }
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
