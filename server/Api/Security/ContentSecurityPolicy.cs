using Microsoft.Extensions.Configuration;

namespace Api.Security;

/// <summary>
/// Builds the SPA's Content-Security-Policy from configuration.
/// <para>
/// The origins come from the same <c>FrontendRuntime</c> section that fills the SPA's
/// runtime config, so the policy describes whatever environment the host is actually
/// running in. They used to be a hardcoded list of <c>dev-*</c> hosts compiled into the
/// middleware, which was correct on dev and would have blocked the SPA from reaching its
/// own API and every sibling service everywhere else. This mirrors the same fix already
/// made in blocks-os (<c>server/Api/Security/ContentSecurityPolicy.cs</c>).
/// </para>
/// </summary>
public static class ContentSecurityPolicy
{
    /// <summary>
    /// <c>FrontendRuntime</c> keys holding an origin the SPA makes requests to. Anything
    /// not derivable from these (a CDN, a third-party service) goes in
    /// <c>Csp:ExtraConnectSrc</c> / <c>Csp:ExtraImgSrc</c> rather than back into code.
    /// </summary>
    internal static readonly string[] ConnectOriginKeys =
    [
        "BLOCKS_IAM_BASE_URL",
        "BLOCKS_CONSTRUCT_URL",
        "BLOCKS_LOCALIZATION_BASE_URL",
        "BLOCKS_AGENTS_BASE_URL",
        "BLOCKS_DATA_BASE_URL",
        "BLOCKS_UTILITIES_BASE_URL",
        "BLOCKS_LOGIC_BASE_URL",
        "BLOCKS_MONITOR_BASE_URL",
        "BLOCKS_RELEASE_BASE_URL",
        "BLOCKS_STUDIO_BASE_URL",
        "BLOCKS_OS_BASE_URL",
    ];

    /// <summary>
    /// Origins the SPA opens a WebSocket to. A CSP source is scheme-sensitive, so
    /// <c>https://host</c> does not permit <c>wss://host</c> and the ws form has to be
    /// emitted separately -- the hardcoded policy listed <c>wss://dev-logic...</c> for
    /// exactly this reason.
    /// </summary>
    internal static readonly string[] WebSocketOriginKeys =
    [
        "BLOCKS_LOGIC_BASE_URL",
    ];

    /// <summary>Where a login POST may be sent: the identity host and the portal.</summary>
    internal static readonly string[] FormActionOriginKeys =
    [
        "BLOCKS_IAM_BASE_URL",
        "BLOCKS_OS_BASE_URL",
    ];

    /// <summary>
    /// reCAPTCHA is loaded by <c>client/app/components/captcha/reCaptcha.tsx</c>, which
    /// injects <c>https://www.google.com/recaptcha/api.js</c> and renders the widget in an
    /// iframe. These are fixed Google origins, identical in every environment, so they stay
    /// in code rather than in per-environment configuration. Without them the previous
    /// policy blocked captcha on login and signup in every environment.
    /// </summary>
    private static readonly string[] CaptchaScriptOrigins =
    [
        "https://www.google.com",
        "https://www.gstatic.com",
    ];

    private static readonly string[] CaptchaFrameOrigins =
    [
        "https://www.google.com",
    ];

    public static string Build(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var runtime = configuration.GetSection("FrontendRuntime");
        var csp = configuration.GetSection("Csp");

        return BuildPolicy(
            connectSrc: Origins(runtime, ConnectOriginKeys)
                .Concat(WebSocketOrigins(runtime, WebSocketOriginKeys))
                .Concat(Split(csp["ExtraConnectSrc"])),
            imgSrc: Split(csp["ExtraImgSrc"]),
            formAction: Origins(runtime, FormActionOriginKeys).Concat(Split(csp["ExtraFormAction"])),
            scriptSrc: CaptchaScriptOrigins.Concat(Split(csp["ExtraScriptSrc"])),
            frameSrc: CaptchaFrameOrigins.Concat(Split(csp["ExtraFrameSrc"])));
    }

    /// <summary>
    /// The policy itself, separated from configuration so it can be asserted directly.
    /// Public rather than internal because the test project has no InternalsVisibleTo.
    /// </summary>
    public static string BuildPolicy(
        IEnumerable<string?> connectSrc,
        IEnumerable<string?> imgSrc,
        IEnumerable<string?> formAction,
        IEnumerable<string?> scriptSrc,
        IEnumerable<string?> frameSrc)
    {
        var connect = Normalize(connectSrc);
        var img = Normalize(imgSrc);
        var form = Normalize(formAction);
        var script = Normalize(scriptSrc);
        var frame = Normalize(frameSrc);

        return string.Join(
            " ",
            "default-src 'self';",
            $"script-src 'self'{Suffix(script)};",

            // Inline styles, both <style> elements and style attributes. blocks-os measured
            // the narrower option (style-src-elem 'self', relaxing only style-src-attr) and
            // found it does not hold: Radix/vaul/sonner/cmdk inject <style> elements at
            // runtime for scroll-lock and positioning, and the login page failed to lay out.
            // Neither form can carry a nonce and the values are unbounded, so hashes are not
            // an option either. script-src stays strict, which is the directive that matters
            // for injection.
            "style-src 'self' 'unsafe-inline';",

            $"img-src 'self' data: blob:{Suffix(img)};",
            "font-src 'self' data:;",
            $"connect-src 'self'{Suffix(connect)};",
            $"frame-src 'self'{Suffix(frame)};",
            "frame-ancestors 'none';",
            "base-uri 'self';",
            "object-src 'none';",
            $"form-action 'self'{Suffix(form)}");
    }

    /// <summary>Reduce configured values to distinct, sorted origins.</summary>
    private static List<string> Normalize(IEnumerable<string?> values) =>
        values
            .Select(ToOrigin)
            .Where(origin => origin is not null)
            .Select(origin => origin!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(origin => origin, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IEnumerable<string?> Origins(IConfiguration section, IEnumerable<string> keys) =>
        keys.Select(key => section[key]);

    /// <summary>Same origins as <see cref="Origins"/>, re-scheme'd to ws/wss.</summary>
    private static IEnumerable<string?> WebSocketOrigins(IConfiguration section, IEnumerable<string> keys) =>
        keys.Select(key => ToWebSocketOrigin(section[key]));

    /// <summary>
    /// A CSP source is an origin, so any path, query or trailing slash is dropped. A value
    /// that is not an absolute http(s)/ws(s) URL is ignored rather than emitted verbatim, so
    /// a malformed secret cannot inject a directive. ws/wss are accepted because
    /// <see cref="ToWebSocketOrigin"/> feeds its output back through here.
    /// </summary>
    public static string? ToOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return null;

        if (uri.Scheme != Uri.UriSchemeHttp
            && uri.Scheme != Uri.UriSchemeHttps
            && uri.Scheme != Uri.UriSchemeWs
            && uri.Scheme != Uri.UriSchemeWss) return null;

        return uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>The ws:// or wss:// form of an http(s) origin, or null if it is not one.</summary>
    public static string? ToWebSocketOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return null;

        if (uri.Scheme == Uri.UriSchemeHttps) return $"wss://{uri.Authority}";
        if (uri.Scheme == Uri.UriSchemeHttp) return $"ws://{uri.Authority}";

        return null;
    }

    /// <summary>Space- or comma-separated list, for origins no runtime key describes.</summary>
    public static IEnumerable<string?> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Suffix(List<string> origins) =>
        origins.Count == 0 ? string.Empty : " " + string.Join(" ", origins);
}
