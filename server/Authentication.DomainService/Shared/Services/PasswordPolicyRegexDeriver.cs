using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Authentication.DomainService.Shared.ResponseModel;

namespace Authentication.DomainService.Shared.Services;

/// <summary>
/// Decodes a tenant's <c>PasswordStrengthCheckerRegex</c> into the structured rule published on
/// the OIDC UI config endpoint.
///
/// Whenever the rule fits the four flags -- the overwhelmingly common case -- the pattern is
/// read here, on the server, and turned into plain booleans and integers. Nothing of it reaches
/// the response and the browser builds no RegExp, which is what the structured policy exists for.
///
/// The decoder parses the pattern's structure -- anchors, a run of lookaheads, then a body with
/// a length quantifier -- and works out what each class *means* by testing it against probe
/// characters, rather than comparing it to a list of expected spellings. So <c>[a-z]</c>,
/// <c>\d</c>, <c>[0-9]</c>, <c>[\d]</c>, <c>[\W_]</c> and <c>[^A-Za-z0-9]</c> are all
/// recognised for what they match, not for how they are written. A class that covers one of the
/// four only partially is not that class: <c>\W</c> and <c>[^\w]</c> exclude <c>_</c>, so they
/// are "every special but underscore" -- near <c>RequireSpecialChars</c>, not equal to it.
///
/// A rule it cannot decode *exactly* is never approximated: a requirement the six structured
/// fields cannot express (say "a letter, either case", "one of !@#$", or "no character three
/// times running") is published as the pattern itself, for the client to show as a single
/// pass/fail requirement. Only a pattern already screened by
/// <see cref="PasswordPolicyRegexValidator"/> -- JavaScript-compatible, no catastrophic
/// backtracking -- is ever passed on that way.
/// </summary>
public static class PasswordPolicyRegexDeriver
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Mirrors the client's own input cap, used as the ceiling for an open-ended rule.</summary>
    public const int UnboundedMaxLength = 256;

    private const int MaxPatternLength = 512;

    private static readonly char[] Lowercase = [.. Enumerable.Range('a', 26).Select(c => (char)c)];
    private static readonly char[] Uppercase = [.. Enumerable.Range('A', 26).Select(c => (char)c)];
    private static readonly char[] Digits = [.. Enumerable.Range('0', 10).Select(c => (char)c)];
    private static readonly char[] Specials =
        [.. " !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~".ToCharArray()];

    /// <summary>The prefixes a "somewhere in the string" lookahead can be written with.</summary>
    private static readonly string[] AnyPrefixes = [".*?", ".*", "[\\s\\S]*?", "[\\s\\S]*", "[^]*"];

    private enum CharacterClass { Lower, Upper, Digit, Special }

    /// <summary>What the structural scan found, before any of it is interpreted.</summary>
    private sealed record Structure(List<string> Assertions, string Body, int MinLength, int MaxLength);

    /// <summary>
    /// Returns the tenant's rule as structured data, the pattern itself when the rule says more
    /// than the flags can, or null when there is no usable rule at all.
    /// </summary>
    public static OidcUiPasswordPolicyResponse? Derive(string? pattern, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;

        var trimmed = pattern.Trim();
        if (trimmed.Length > MaxPatternLength) return null;

        var structure = TryParseStructure(trimmed);
        var policy = structure is null ? null : TryDecode(structure);

        if (policy is not null)
        {
            policy.Message = Blank(message) ? null : message;
            return policy;
        }

        // The rule is real but not expressible in the four flags. Hand the client the pattern so
        // it can still tell the user pass or fail -- but only one already screened as safe.
        if (PasswordPolicyRegexValidator.Validate(trimmed) is not null) return null;

        return new OidcUiPasswordPolicyResponse
        {
            // Length still comes from the pattern when the scan could read it; otherwise the
            // client's own input cap is the only bound anyone can state.
            MinLength = structure?.MinLength ?? 1,
            MaxLength = structure?.MaxLength ?? UnboundedMaxLength,
            RequireUppercase = false,
            RequireLowercase = false,
            RequireNumbers = false,
            RequireSpecialChars = false,
            Message = Blank(message) ? null : message,
            Pattern = trimmed
        };
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Reads the shape "anchor, assertions, body, quantifier, anchor" without interpreting any of
    /// it. Succeeds for patterns whose requirements are unreadable, so the length bounds of those
    /// are still known.
    /// </summary>
    private static Structure? TryParseStructure(string pattern)
    {
        var rest = pattern;
        if (!TryStripAnchors(ref rest)) return null;

        var assertions = new List<string>();
        var index = 0;

        while (index < rest.Length && rest[index] == '(')
        {
            if (!TryFindGroupEnd(rest, index, out var end)) return null;

            // A group that is not an assertion at all (a capture, an alternation) changes what the
            // body is, so the scan cannot claim to know the length.
            var opener = rest.AsSpan(index);
            if (!opener.StartsWith("(?=") && !opener.StartsWith("(?!")) return null;

            assertions.Add(rest[index..(end + 1)]);
            index = end + 1;
        }

        var tail = rest[index..];
        var cursor = 0;
        if (!TryReadAtom(tail, ref cursor, out var body)) return null;
        if (!TryReadQuantifier(tail[cursor..], out var minLength, out var maxLength)) return null;

        return new Structure(assertions, body, minLength, maxLength);
    }

    /// <summary>The four flags, or null when any part of the rule cannot be said with them.</summary>
    private static OidcUiPasswordPolicyResponse? TryDecode(Structure structure)
    {
        if (!IsPermissive(structure.Body)) return null;

        var required = new HashSet<CharacterClass>();
        foreach (var assertion in structure.Assertions)
        {
            // A negative assertion is a prohibition; the flags can only require.
            if (assertion.StartsWith("(?!", StringComparison.Ordinal)) return null;
            if (!TryFindGroupEnd(assertion, 0, out var end)) return null;
            if (!TryClassifyLookahead(assertion[3..end], out var characterClass)) return null;

            required.Add(characterClass.Value);
        }

        return new OidcUiPasswordPolicyResponse
        {
            MinLength = structure.MinLength,
            MaxLength = structure.MaxLength,
            RequireLowercase = required.Contains(CharacterClass.Lower),
            RequireUppercase = required.Contains(CharacterClass.Upper),
            RequireNumbers = required.Contains(CharacterClass.Digit),
            RequireSpecialChars = required.Contains(CharacterClass.Special)
        };
    }

    private static bool TryStripAnchors(ref string pattern)
    {
        var start = pattern.StartsWith('^') ? 1 : pattern.StartsWith(@"\A", StringComparison.Ordinal) ? 2 : 0;
        if (start == 0) return false;

        var end = pattern.EndsWith('$') ? 1
            : pattern.EndsWith(@"\z", StringComparison.Ordinal) || pattern.EndsWith(@"\Z", StringComparison.Ordinal) ? 2
            : 0;
        if (end == 0) return false;

        // A "$" that is itself escaped is a literal dollar sign, not an anchor.
        if (pattern.EndsWith('$') && IsEscaped(pattern, pattern.Length - 1)) return false;

        pattern = pattern[start..^end];
        return true;
    }

    private static bool IsEscaped(string s, int position)
    {
        var backslashes = 0;
        for (var i = position - 1; i >= 0 && s[i] == '\\'; i--) backslashes++;
        return backslashes % 2 == 1;
    }

    /// <summary>Finds the ")" closing the group at <paramref name="start"/>, ignoring bracket contents.</summary>
    private static bool TryFindGroupEnd(string s, int start, out int end)
    {
        end = -1;
        var depth = 0;
        var inClass = false;

        for (var i = start; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\\') { i++; continue; }

            if (inClass)
            {
                if (c == ']') inClass = false;
                continue;
            }

            switch (c)
            {
                case '[': inClass = true; break;
                case '(': depth++; break;
                case ')':
                    depth--;
                    if (depth == 0) { end = i; return true; }
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads "(?=<i>anything</i> CLASS <i>anything</i>)" and reports which class it asserts.
    /// </summary>
    private static bool TryClassifyLookahead(string inner, [NotNullWhen(true)] out CharacterClass? characterClass)
    {
        characterClass = null;

        var prefix = AnyPrefixes.FirstOrDefault(p => inner.StartsWith(p, StringComparison.Ordinal));
        if (prefix is null) return false;

        var cursor = prefix.Length;
        if (!TryReadAtom(inner, ref cursor, out var atom)) return false;

        // A trailing ".*" is redundant but common; anything else is a second assertion.
        var trailer = inner[cursor..];
        if (trailer.Length > 0 && !AnyPrefixes.Contains(trailer, StringComparer.Ordinal)) return false;

        return TryClassifyAtom(atom, out characterClass);
    }

    /// <summary>
    /// Works out which of the four classes an atom is, by what it actually matches. It must cover
    /// one class completely and admit nothing outside it -- a partial or overlapping class (say
    /// <c>[a-zA-Z]</c> or <c>[!@#$]</c>) is not expressible in the structured fields.
    /// </summary>
    private static bool TryClassifyAtom(string atom, [NotNullWhen(true)] out CharacterClass? characterClass)
    {
        characterClass = null;
        if (!TryCompile(atom, out var regex)) return false;

        var lower = Count(regex, Lowercase);
        var upper = Count(regex, Uppercase);
        var digit = Count(regex, Digits);
        var special = Count(regex, Specials);

        characterClass =
            Covers(lower, Lowercase) && upper == 0 && digit == 0 && special == 0 ? CharacterClass.Lower
            : Covers(upper, Uppercase) && lower == 0 && digit == 0 && special == 0 ? CharacterClass.Upper
            : Covers(digit, Digits) && lower == 0 && upper == 0 && special == 0 ? CharacterClass.Digit
            : Covers(special, Specials) && lower == 0 && upper == 0 && digit == 0 ? CharacterClass.Special
            : null;

        return characterClass is not null;
    }

    /// <summary>A body must admit every character, or it bans some the structured fields cannot ban.</summary>
    private static bool IsPermissive(string atom)
    {
        if (!TryCompile(atom, out var regex)) return false;

        return Covers(Count(regex, Lowercase), Lowercase)
            && Covers(Count(regex, Uppercase), Uppercase)
            && Covers(Count(regex, Digits), Digits)
            && Covers(Count(regex, Specials), Specials);
    }

    private static bool TryCompile(string atom, [NotNullWhen(true)] out Regex? regex)
    {
        regex = null;
        try
        {
            regex = new Regex($"^(?:{atom})$", RegexOptions.None, MatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static int Count(Regex regex, char[] characters)
    {
        try
        {
            return characters.Count(c => regex.IsMatch(c.ToString()));
        }
        catch (RegexMatchTimeoutException)
        {
            return -1;
        }
    }

    private static bool Covers(int matched, char[] characters) => matched == characters.Length;

    /// <summary>Reads one regex atom: an escape, a bracket expression, or ".".</summary>
    private static bool TryReadAtom(string s, ref int i, [NotNullWhen(true)] out string? atom)
    {
        atom = null;
        if (i >= s.Length) return false;

        var start = i;
        switch (s[i])
        {
            case '\\':
                if (i + 1 >= s.Length) return false;
                i += 2;
                break;

            case '[':
                i++;
                if (i < s.Length && s[i] == '^') i++;
                if (i < s.Length && s[i] == ']') i++; // a leading "]" is a literal
                while (i < s.Length && s[i] != ']')
                {
                    if (s[i] == '\\') i++;
                    i++;
                }
                if (i >= s.Length) return false;
                i++;
                break;

            case '.':
                i++;
                break;

            default:
                return false;
        }

        atom = s[start..i];
        return true;
    }

    /// <summary>Reads the length quantifier: "{m,n}", "{m,}", "{m}", "+" or "*".</summary>
    private static bool TryReadQuantifier(string s, out int minLength, out int maxLength)
    {
        minLength = 0;
        maxLength = 0;

        switch (s)
        {
            case "+":
                minLength = 1;
                maxLength = UnboundedMaxLength;
                return true;
            case "*":
                minLength = 0;
                maxLength = UnboundedMaxLength;
                return true;
        }

        if (s.Length < 3 || s[0] != '{' || s[^1] != '}') return false;

        var bounds = s[1..^1].Split(',');
        if (bounds.Length > 2) return false;
        if (!int.TryParse(bounds[0], out minLength) || minLength < 0) return false;

        if (bounds.Length == 1)
        {
            maxLength = minLength;
        }
        else if (bounds[1].Length == 0)
        {
            // "{8,}" has no upper bound; the client's own input cap is the effective ceiling.
            maxLength = Math.Max(UnboundedMaxLength, minLength);
        }
        else if (!int.TryParse(bounds[1], out maxLength))
        {
            return false;
        }

        return maxLength >= minLength;
    }
}
