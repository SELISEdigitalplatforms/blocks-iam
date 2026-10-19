using System.Text.RegularExpressions;

namespace Authentication.DomainService.Utilities
{
    /// <summary>
    /// The two kinds of redirect URI an OIDC client may register: a web URL, or a native app's
    /// private-use URI scheme (RFC 8252 section 7.1). The SPA's <c>isAppRedirectUrl</c> applies
    /// the same app-scheme checks, so the two must change together.
    /// </summary>
    public static class RedirectUriSchemes
    {
        // RFC 3986 scheme syntax; the dot is checked separately so that the reverse-domain
        // requirement reads on its own.
        private static readonly Regex SchemePattern = new("^[A-Za-z][A-Za-z0-9+.-]*$", RegexOptions.Compiled);

        // Schemes that would run script, read local content, or point back at the web.
        private static readonly HashSet<string> RefusedSchemes = new(StringComparer.OrdinalIgnoreCase)
        {
            "http", "https", "javascript", "data", "file", "vbscript", "blob", "about"
        };

        /// <summary>
        /// An absolute http(s) URL.
        /// </summary>
        public static bool IsWebUri(string? value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// An absolute URI whose scheme is a reverse domain name (for example
        /// <c>com.example.app:/oauth2redirect</c>), with no fragment.
        /// </summary>
        public static bool IsAppScheme(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out _))
            {
                return false;
            }

            var colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                return false;
            }

            var scheme = value[..colon];
            if (!SchemePattern.IsMatch(scheme) || !scheme.Contains('.', StringComparison.Ordinal))
            {
                return false;
            }

            if (RefusedSchemes.Contains(scheme))
            {
                return false;
            }

            return !value.Contains('#', StringComparison.Ordinal);
        }
    }
}
