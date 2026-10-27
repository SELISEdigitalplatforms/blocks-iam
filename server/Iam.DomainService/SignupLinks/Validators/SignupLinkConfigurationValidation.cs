using System.Globalization;
using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Services;

namespace Iam.DomainService.SignupLinks;

/// <summary>
/// Where the OIDC client and redirect being checked came from, which decides what the caller is
/// told to fix.
/// </summary>
internal enum SignupLinkClientSource
{
    /// <summary>Stored on the configuration and not changed by this request.</summary>
    StoredConfiguration,

    /// <summary>Being chosen now: a configuration created, or its client or redirect changed.</summary>
    ChosenNow,

    /// <summary>Sent by a generate request in place of the configuration's pair.</summary>
    GenerateRequest
}

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

    /// <summary>
    /// What stops an OIDC link from finishing against this client and redirect -- the field to
    /// report it on and the message -- or null when nothing does. Redemption refuses all of these
    /// with a bare invalid_link, so saving a configuration and generating a link refuse them
    /// first, where the caller can be told what to fix.
    /// </summary>
    public static (string Field, string Message)? FindClientProblem(
        OidcClientRegistrationInfo? client,
        string clientId,
        string redirectUri,
        SignupLinkClientSource source)
    {
        var text = source switch
        {
            SignupLinkClientSource.ChosenNow => ChosenNowText,
            SignupLinkClientSource.GenerateRequest => GenerateRequestText,
            _ => StoredConfigurationText
        };

        if (client == null)
        {
            return ("ClientId", Describe(text.Missing, clientId, redirectUri));
        }

        if (!client.IsActive)
        {
            return ("ClientId", Describe(text.Disabled, clientId, redirectUri));
        }

        if (!client.RedirectUris.Any(u => string.Equals(u, redirectUri, StringComparison.Ordinal)))
        {
            return ("RedirectUri", Describe(text.Redirect, clientId, redirectUri));
        }

        return client.IdentityProvider switch
        {
            OidcIdentityProviderState.Active => null,
            OidcIdentityProviderState.Inactive => ("ClientId", Describe(text.ProviderInactive, clientId, redirectUri)),
            _ => ("ClientId", Describe(text.NotProvider, clientId, redirectUri))
        };
    }

    private static string Describe(string template, string clientId, string redirectUri) =>
        string.Format(CultureInfo.InvariantCulture, template, clientId, redirectUri);

    /// <summary>{0} is the client id, {1} the redirect URI.</summary>
    private sealed record ClientProblemText(
        string Missing,
        string Disabled,
        string Redirect,
        string NotProvider,
        string ProviderInactive);

    // Saved while the client was fine; something happened to the client since.
    private static readonly ClientProblemText StoredConfigurationText = new(
        "The OIDC client '{0}' no longer exists. Update the signup link configuration to use an existing client.",
        "The OIDC client '{0}' is disabled. Enable it or update the signup link configuration.",
        "The redirect URI '{1}' is no longer registered on OIDC client '{0}'. Update the signup link configuration.",
        "The OIDC client '{0}' is not registered as a Blocks OIDC identity provider. Register it or update the signup link configuration.",
        "The Blocks OIDC identity provider for client '{0}' is inactive. Activate it or update the signup link configuration.");

    private static readonly ClientProblemText ChosenNowText = new(
        "The OIDC client '{0}' does not exist. Choose an existing client.",
        "The OIDC client '{0}' is disabled. Enable it or choose another client.",
        "The redirect URI '{1}' is not registered on OIDC client '{0}'. Choose one of its registered redirect URIs.",
        "The OIDC client '{0}' is not registered as a Blocks OIDC identity provider. Register it or choose another client.",
        "The Blocks OIDC identity provider for client '{0}' is inactive. Activate it or choose another client.");

    private static readonly ClientProblemText GenerateRequestText = new(
        "The OIDC client '{0}' in this request does not exist.",
        "The OIDC client '{0}' in this request is disabled.",
        "The redirect URI '{1}' in this request is not registered on OIDC client '{0}'.",
        "The OIDC client '{0}' in this request is not registered as a Blocks OIDC identity provider.",
        "The Blocks OIDC identity provider for client '{0}' in this request is inactive.");

    /// <summary>Absent mode means Oidc, so callers predating embedded mode keep working.</summary>
    public static SignupLinkMode Resolve(SignupLinkMode? mode) => mode ?? SignupLinkMode.Oidc;

    public static bool IsOidc(SignupLinkMode? mode) => Resolve(mode) == SignupLinkMode.Oidc;
}
