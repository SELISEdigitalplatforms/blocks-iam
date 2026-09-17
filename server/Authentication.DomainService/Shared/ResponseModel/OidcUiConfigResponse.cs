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
    /// The tenant's password rule, always as plain data. The four flags and the length bounds say
    /// the whole rule whenever they can; anything they cannot say is checked by the server through
    /// <see cref="RequiresServerCheck"/>. No regular expression is ever published.
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

        /// <summary>
        /// True when the tenant's rule says something the flags above cannot -- "a letter, either
        /// case", "one of !@#$", "no character three times running", a length only an assertion
        /// states. The rule itself is never published: a public, unauthenticated endpoint would
        /// otherwise hand out whatever the pattern happens to encode, including blacklisted terms
        /// and internal naming conventions.
        ///
        /// The client shows one extra pass/fail requirement and asks
        /// <c>POST /api/idp/password-check</c> to evaluate it, which runs the same check the
        /// account endpoints enforce on submit.
        /// </summary>
        public bool RequiresServerCheck { get; set; }
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
