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

    public static bool IsAllowedMaxRedemptions(int? value) => value is null or 1;
}
