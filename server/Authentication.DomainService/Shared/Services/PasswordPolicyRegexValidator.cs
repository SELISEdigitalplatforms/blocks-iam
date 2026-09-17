using System.Text.RegularExpressions;

namespace Authentication.DomainService.Shared.Services;

/// <summary>Save-time checks for newly supplied public password rules, never for password enforcement.</summary>
public static class PasswordPolicyRegexValidator
{
    private static readonly string[] UnsupportedTokens =
    [
        "(?(", "(?#", "(?'", "(?i", "(?m", "(?s", "(?x", "(?n", "(?-",
        @"\A", @"\Z", @"\z", @"\G", @"\p{", @"\P{", "-["
    ];

    private static readonly string[] Probes =
    [
        string.Empty, new string('a', 256), string.Concat(Enumerable.Repeat("aA1!", 64)),
        new string('a', 255) + "!", new string('a', 255) + " "
    ];

    /// <summary>Returns the first error code, or null for a valid or omitted rule.</summary>
    public static string? Validate(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        if (pattern.Length > 512) return "PasswordStrengthCheckerRegex_Too_Long";

        try { _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
        catch (ArgumentException) { return "PasswordStrengthCheckerRegex_Invalid_Syntax"; }

        // Deliberately a literal-token scan, not a parser or a rewrite of the stored rule.
        if (UnsupportedTokens.Any(token => pattern.Contains(token, StringComparison.Ordinal))
            || Regex.IsMatch(pattern, @"\(\?<[A-Za-z0-9_]*-"))
            return "PasswordStrengthCheckerRegex_Not_Javascript_Compatible";

        var regex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        try
        {
            foreach (var probe in Probes) regex.IsMatch(probe);
        }
        catch (RegexMatchTimeoutException) { return "PasswordStrengthCheckerRegex_Too_Slow"; }

        return null;
    }
}
