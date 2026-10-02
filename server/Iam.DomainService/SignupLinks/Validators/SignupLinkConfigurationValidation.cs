namespace Iam.DomainService.SignupLinks;

internal static class SignupLinkConfigurationValidation
{
    public const int MinLifetimeMinutes = 5;
    public const int MaxLifetimeMinutes = 10080;
    public const int MaxPermissions = 50;
    public const int DefaultLifetimeMinutes = 1440;

    public static bool IsRelativeForwardedTo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        // Relative path: must start with a single "/", must not be protocol-relative ("//…"),
        // and must not carry a URI scheme. Do not use UriKind.Absolute — on Unix a path like
        // "/projects" parses as an absolute file URI and would be rejected incorrectly.
        if (!value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (value.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    /// <summary>null = not specified, 0 = unlimited, otherwise a positive cap.</summary>
    public static bool IsAllowedMaxRedemptions(int? value) => value is null or >= 0;


    /// <summary>
    /// A join URL is absolute https with no query and no fragment. The fragment is excluded
    /// because the code is appended as one; a query is excluded so the composed link cannot
    /// smuggle parameters into the construct's join screen.
    /// </summary>
    public static bool IsValidJoinUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    /// <summary>Absent mode means Oidc, so callers predating embedded mode keep working.</summary>
    public static SignupLinkMode Resolve(SignupLinkMode? mode) => mode ?? SignupLinkMode.Oidc;

    public static bool IsOidc(SignupLinkMode? mode) => Resolve(mode) == SignupLinkMode.Oidc;
}
