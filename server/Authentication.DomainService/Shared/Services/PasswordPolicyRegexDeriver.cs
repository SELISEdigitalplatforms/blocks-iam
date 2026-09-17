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
/// A rule it cannot decode *exactly* is never approximated, and never published either: this
/// endpoint is public and unauthenticated, so a pattern on the wire would hand out whatever it
/// happens to encode -- blacklisted terms, internal naming conventions. Such a rule is flagged
/// <c>RequiresServerCheck</c> instead, and the client asks the server to evaluate it.
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
    /// <summary>
    /// What the structural scan found, before any of it is interpreted. Length can come from two
    /// places -- the body's own quantifier, and a "(?=.{m,n}$)" assertion -- and only the body's
    /// counts when the body admits every character; a narrow body like "\S" bounds a different
    /// string than the whole password.
    /// </summary>
    private sealed record Structure(
        List<string> Assertions,
        string Body,
        int BodyMinLength,
        int BodyMaxLength,
        int? AssertedMinLength,
        int? AssertedMaxLength);

    /// <summary>
    /// Returns the tenant's rule as structured data, the pattern itself when the rule says more
    /// than the flags can, or null when there is no usable rule at all.
    /// </summary>
    public static OidcUiPasswordPolicyResponse? Derive(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;

        var trimmed = pattern.Trim();
        if (trimmed.Length > MaxPatternLength) return null;

        var structure = TryParseStructure(trimmed);
        var bounds = EffectiveBounds(structure);
        var policy = structure is null ? null : TryDecode(structure, bounds);

        if (policy is not null) return policy;

        // The rule is real but not expressible in the four flags. The pattern stays on the
        // server -- this endpoint is public -- and the client asks the server to check it.
        return new OidcUiPasswordPolicyResponse
        {
            // Whatever the scan could read is still worth saying. Zero means "nothing readable",
            // which the client renders as no length row rather than as an invented one.
            MinLength = bounds.Min,
            MaxLength = bounds.Max,
            RequireUppercase = false,
            RequireLowercase = false,
            RequireNumbers = false,
            RequireSpecialChars = false,
            RequiresServerCheck = true
        };
    }


    /// <summary>
    /// The length the whole password must have, from whichever readings of the pattern actually
    /// bound it -- intersected when both do. Zero means nothing trustworthy was found, which the
    /// client renders as no length requirement rather than as a made-up one.
    /// </summary>
    private static (int Min, int Max) EffectiveBounds(Structure? structure)
    {
        if (structure is null) return (0, 0);

        int? min = structure.AssertedMinLength;
        int? max = structure.AssertedMaxLength;

        // The body's quantifier only bounds the password when the body admits every character.
        if (IsPermissive(structure.Body))
        {
            min = min is null ? structure.BodyMinLength : Math.Max(min.Value, structure.BodyMinLength);
            max = max is null ? structure.BodyMaxLength : Math.Min(max.Value, structure.BodyMaxLength);
        }

        return min is null || max is null || min > max ? (0, 0) : (min.Value, max.Value);
    }

    /// <summary>
    /// Reads a pure length assertion -- "(?=.{10,32}$)" and its spellings -- which constrains the
    /// whole password rather than asserting any character class.
    /// </summary>
    private static bool TryReadLengthAssertion(string inner, out int minLength, out int maxLength)
    {
        minLength = 0;
        maxLength = 0;

        var body = inner.StartsWith('^') ? inner[1..] : inner;
        if (!body.EndsWith('$')) return false;
        body = body[..^1];

        var cursor = 0;
        if (!TryReadAtom(body, ref cursor, out var atom)) return false;

        // Only a body that admits everything makes this a length rule and nothing else.
        if (!IsPermissive(atom)) return false;

        return TryReadQuantifier(body[cursor..], out minLength, out maxLength);
    }

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
        int? assertedMin = null;
        int? assertedMax = null;
        var index = 0;

        while (index < rest.Length && rest[index] == '(')
        {
            if (!TryFindGroupEnd(rest, index, out var end)) return null;

            // A group that is not an assertion at all (a capture, an alternation) changes what the
            // body is, so the scan cannot claim to know the length.
            var opener = rest.AsSpan(index);
            if (!opener.StartsWith("(?=") && !opener.StartsWith("(?!")) return null;

            // A length assertion is read as length rather than kept as a requirement: it is the
            // one assertion the structured fields can say in full.
            if (opener.StartsWith("(?=")
                && TryReadLengthAssertion(rest[(index + 3)..end], out var min, out var max))
            {
                assertedMin = assertedMin is null ? min : Math.Max(assertedMin.Value, min);
                assertedMax = assertedMax is null ? max : Math.Min(assertedMax.Value, max);
            }
            else
            {
                assertions.Add(rest[index..(end + 1)]);
            }

            index = end + 1;
        }

        var tail = rest[index..];
        var cursor = 0;
        if (!TryReadAtom(tail, ref cursor, out var body)) return null;
        if (!TryReadQuantifier(tail[cursor..], out var minLength, out var maxLength)) return null;

        return new Structure(assertions, body, minLength, maxLength, assertedMin, assertedMax);
    }

    /// <summary>The four flags, or null when any part of the rule cannot be said with them.</summary>
    private static OidcUiPasswordPolicyResponse? TryDecode(Structure structure, (int Min, int Max) bounds)
    {
        // A rule whose length cannot be stated is not fully described by the flags.
        if (bounds.Min <= 0) return null;

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
            MinLength = bounds.Min,
            MaxLength = bounds.Max,
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
