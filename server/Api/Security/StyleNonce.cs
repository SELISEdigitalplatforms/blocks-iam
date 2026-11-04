using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Api.Security;

/// <summary>
/// The CSP style nonce for a browser.
/// <para>
/// A fresh nonce on every response changes the HTML body on every request, and a scanner
/// comparing two responses for the same page (ZAP rule 10104, User Agent Fuzzer) then reports
/// every page. So the nonce is derived from a random per-browser seed held in an HttpOnly
/// cookie, keyed with a secret that never leaves the process. The same browser gets the same
/// shell; another browser, or the same one after the cookie expires, gets a different nonce.
/// A page cannot read the seed, and the nonce itself is hidden from CSS and the DOM.
/// </para>
/// </summary>
public sealed class StyleNonce
{
    /// <summary>Host-only, Secure, HttpOnly cookie carrying the per-browser seed.</summary>
    public const string SeedCookieName = "__Host-csp-seed";

    /// <summary>The seed is rotated by expiry, which also rotates the nonce.</summary>
    public static readonly TimeSpan SeedLifetime = TimeSpan.FromHours(12);

    private const int SeedBytes = 16;
    private readonly byte[] _key;

    public StyleNonce(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32) throw new ArgumentException("Key must be at least 256 bits.", nameof(key));
        _key = key;
    }

    /// <summary>A process-local key: every response from this process uses it.</summary>
    public static StyleNonce CreateWithRandomKey() => new(RandomNumberGenerator.GetBytes(32));

    /// <summary>A new random seed, base64url without padding (cookie-safe).</summary>
    public static string NewSeed() => Base64Url(RandomNumberGenerator.GetBytes(SeedBytes));

    /// <summary>True for a value <see cref="NewSeed"/> could have produced.</summary>
    public static bool IsValidSeed(string? seed)
    {
        if (string.IsNullOrEmpty(seed) || seed.Length != 22) return false;
        foreach (var c in seed)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_')) return false;
        }
        return true;
    }

    /// <summary>The nonce for a seed: 128 bits of HMAC-SHA256(key, seed), base64.</summary>
    public string For(string seed)
    {
        if (!IsValidSeed(seed)) throw new ArgumentException("Invalid seed.", nameof(seed));
        var mac = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(seed));
        return Convert.ToBase64String(mac, 0, 16);
    }

    /// <summary>
    /// The nonce for one response. A request without a valid seed that may render the SPA shell
    /// gets a new seed cookie; API calls and static files get a throwaway nonce and no cookie.
    /// </summary>
    public string ForRequest(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var seed = context.Request.Cookies[SeedCookieName];
        if (IsValidSeed(seed)) return For(seed!);

        seed = NewSeed();
        if (MayServeShell(context.Request.Path.Value))
        {
            context.Response.Cookies.Append(SeedCookieName, seed, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = SeedLifetime,
                IsEssential = true,
            });
        }
        return For(seed);
    }

    /// <summary>True for "/index.html" and extension-less paths outside /api (SPA routes).</summary>
    public static bool MayServeShell(string? path)
    {
        path ??= "";
        if (string.Equals(path, "/index.html", StringComparison.OrdinalIgnoreCase)) return true;
        if (Path.HasExtension(path)) return false;
        return !(string.Equals(path, "/api", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
