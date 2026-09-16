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
    /// The tenant's password rule. Normally plain data: the four flags and the length bounds say
    /// the whole rule, and <see cref="Pattern"/> is null.
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
        /// The tenant's own pattern, sent ONLY when the rule says something these four flags
        /// cannot -- "a letter, either case", "one of !@#$", "no character three times running".
        /// The client shows those as a single pass/fail requirement rather than dropping to a
        /// hard-coded default that describes a different rule.
        ///
        /// Null whenever the flags are sufficient, which is the common case: a pattern goes on
        /// the wire only when it buys the user something. Anything published here has passed
        /// <see cref="Shared.Services.PasswordPolicyRegexValidator"/> at save time, which rejects
        /// patterns that are not JavaScript-compatible or that backtrack catastrophically.
        /// </summary>
        public string? Pattern { get; set; }

        /// <summary>
        /// True when the tenant's rule could be neither decoded into the flags nor safely handed
        /// over as <see cref="Pattern"/> -- a pattern the save-time screening refuses to publish
        /// (not JavaScript-compatible, or catastrophically slow).
        ///
        /// The rule is still enforced on submit, so the client must not fall back to its own
        /// baseline here: that would state requirements nobody configured. It shows whatever the
        /// other fields do say -- the length bounds when those were readable, nothing when they
        /// were not -- and lets the server's own error report the failure.
        /// </summary>
        public bool HasUndescribedRules { get; set; }
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
