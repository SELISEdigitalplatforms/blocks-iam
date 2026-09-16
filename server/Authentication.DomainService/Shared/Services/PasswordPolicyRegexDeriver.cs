using System.Diagnostics.CodeAnalysis;
using Authentication.DomainService.Shared.ResponseModel;

namespace Authentication.DomainService.Shared.Services;

/// <summary>
/// Derives the structured password rule from a tenant's legacy <c>PasswordStrengthCheckerRegex</c>,
/// so a tenant that never migrated to the structured policy still publishes something on the OIDC
/// UI config endpoint instead of nothing.
///
/// This does not weaken the "no regex from network data" rule the structured policy exists for:
/// the pattern is read here, on the server, and only ever turned into plain booleans and integers.
/// No pattern text reaches the response, and the browser still builds no <c>RegExp</c>.
///
/// Deliberately a narrow, literal recogniser rather than a regex parser. It accepts the one shape
/// these rules are written in -- optional class lookaheads followed by a permissive body with a
/// length quantifier -- and returns null for anything it does not fully understand. A rule it
/// cannot read publishes no policy, exactly as before; it never guesses, because a guess would
/// show the user requirements the server does not actually enforce.
/// </summary>
public static class PasswordPolicyRegexDeriver
{
    /// <summary>Lookaheads, by the character class each one asserts is present.</summary>
    private static readonly (string Token, string Class)[] Lookaheads =
    [
        ("(?=.*[a-z])", "lower"),
        ("(?=.*[A-Z])", "upper"),
        ("(?=.*[0-9])", "digit"),
        ("(?=.*\\d)", "digit"),
        ("(?=.*[\\d])", "digit"),
        ("(?=.*[\\W_])", "special"),
        ("(?=.*[\\W])", "special"),
        ("(?=.*\\W)", "special"),
        ("(?=.*[^A-Za-z0-9])", "special"),
        ("(?=.*[^a-zA-Z0-9])", "special")
    ];

    /// <summary>
    /// Bodies that admit every character. A body narrower than this (say <c>[a-z]</c>) bans
    /// characters the six structured fields cannot express, so it is not derivable at all.
    /// </summary>
    private static readonly HashSet<string> PermissiveBodies = new(StringComparer.Ordinal)
    {
        ".", "[\\s\\S]", "[\\w\\W]",
        "[A-Za-z\\d\\W_]", "[a-zA-Z\\d\\W_]",
        "[A-Za-z0-9\\W_]", "[a-zA-Z0-9\\W_]"
    };

    /// <summary>Returns the equivalent structured rule, or null when the pattern is not derivable.</summary>
    public static OidcUiPasswordPolicyResponse? Derive(string? pattern, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;

        var rest = pattern.Trim();

        // Anchored at both ends, or the length quantifier would not bound the whole password.
        if (!rest.StartsWith('^') || !rest.EndsWith('$')) return null;
        rest = rest[1..^1];

        var required = new HashSet<string>(StringComparer.Ordinal);
        bool matched;
        do
        {
            matched = false;
            foreach (var (token, characterClass) in Lookaheads)
            {
                if (!rest.StartsWith(token, StringComparison.Ordinal)) continue;

                // A rule repeating the same assertion is still the same rule, so a duplicate is
                // not a reason to give up -- but an unrecognised lookahead further on is.
                required.Add(characterClass);
                rest = rest[token.Length..];
                matched = true;
                break;
            }
        } while (matched);

        // Nothing else may assert anything: a lookahead this recogniser does not know is a
        // requirement that would go missing from the derived policy.
        if (rest.Contains("(?", StringComparison.Ordinal)) return null;

        if (!TrySplitQuantifier(rest, out var body, out var minLength, out var maxLength)) return null;
        if (!PermissiveBodies.Contains(body)) return null;

        return new OidcUiPasswordPolicyResponse
        {
            MinLength = minLength,
            MaxLength = maxLength,
            RequireLowercase = required.Contains("lower"),
            RequireUppercase = required.Contains("upper"),
            RequireNumbers = required.Contains("digit"),
            RequireSpecialChars = required.Contains("special"),
            Message = string.IsNullOrWhiteSpace(message) ? null : message
        };
    }

    /// <summary>Splits a trailing <c>{m,n}</c> or <c>{n}</c> off the body. Open-ended is not derivable.</summary>
    private static bool TrySplitQuantifier(
        string input, [NotNullWhen(true)] out string? body, out int minLength, out int maxLength)
    {
        body = null;
        minLength = 0;
        maxLength = 0;

        if (!input.EndsWith('}')) return false;

        var open = input.LastIndexOf('{');
        if (open <= 0) return false;

        var bounds = input[(open + 1)..^1].Split(',');
        if (bounds.Length > 2) return false;

        if (!int.TryParse(bounds[0], out minLength) || minLength < 0) return false;

        // "{8,}" has no upper bound, and MaxLength has no way to say "unbounded".
        maxLength = bounds.Length == 1
            ? minLength
            : int.TryParse(bounds[1], out var parsedMax) ? parsedMax : -1;
        if (maxLength < minLength) return false;

        body = input[..open];
        return true;
    }
}
