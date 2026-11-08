using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.Services
{
    /// <summary>
    /// The endpoints of the built-in social sign-ins. A Google or Microsoft entry of the social type
    /// always uses these, whatever discovery document it names: the login services call Google's
    /// userinfo with the access token in the query and Microsoft Graph's /me with a $select, and no
    /// discovery document describes either. A Microsoft entry may name its own Entra tenant through
    /// that tenant's discovery URL; it then signs users in through that tenant instead of "common".
    /// </summary>
    public static class SocialProviderEndpoints
    {
        public const string SocialProviderType = "social";
        public const string Google = "google";
        public const string Microsoft = "microsoft";
        public const string MicrosoftDefaultTenant = "common";

        private const string MicrosoftHost = "login.microsoftonline.com";
        private const string MicrosoftDiscoveryPath = "v2.0/.well-known/openid-configuration";

        internal sealed record Endpoints(string WellKnownUrl, string AuthorizationUrl, string TokenUrl, string UserInfoUrl);

        internal static Endpoints GoogleEndpoints { get; } = new(
            "https://accounts.google.com/.well-known/openid-configuration",
            "https://accounts.google.com/o/oauth2/v2/auth",
            "https://oauth2.googleapis.com/token",
            "https://www.googleapis.com/oauth2/v1/userinfo?access_token={0}");

        internal static Endpoints MicrosoftEndpoints(string tenant) => new(
            $"https://{MicrosoftHost}/{tenant}/{MicrosoftDiscoveryPath}",
            $"https://{MicrosoftHost}/{tenant}/oauth2/v2.0/authorize",
            $"https://{MicrosoftHost}/{tenant}/oauth2/v2.0/token",
            "https://graph.microsoft.com/v1.0/me");

        /// <summary>A Google or Microsoft entry of the social type.</summary>
        public static bool IsBuiltIn(string? providerType, string? provider) =>
            IsSocial(providerType) && (IsNamed(provider, Google) || IsNamed(provider, Microsoft));

        /// <summary>A Microsoft entry of the social type.</summary>
        public static bool IsMicrosoft(string? providerType, string? provider) =>
            IsSocial(providerType) && IsNamed(provider, Microsoft);

        public static bool IsSocial(string? providerType) =>
            string.Equals(providerType, SocialProviderType, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Reads the tenant out of https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration.
        /// The tenant is a directory id, a verified domain, or common / organizations / consumers. Any
        /// other URL answers false.
        /// </summary>
        public static bool TryReadMicrosoftTenant(string? discoveryLocation, out string tenant)
        {
            tenant = string.Empty;

            if (!Uri.TryCreate(discoveryLocation, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !uri.IsDefaultPort
                || !string.Equals(uri.Host, MicrosoftHost, StringComparison.OrdinalIgnoreCase)
                || uri.Query.Length > 0
                || uri.Fragment.Length > 0)
            {
                return false;
            }

            var path = uri.AbsolutePath.Trim('/');
            var slash = path.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0 || !string.Equals(path[(slash + 1)..], MicrosoftDiscoveryPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var segment = path[..slash];
            if (!segment.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '.'))
            {
                return false;
            }

            tenant = segment;
            return true;
        }

        /// <summary>
        /// Points a built-in social entry at its endpoints: Google's, or Microsoft's for the tenant its
        /// discovery URL names ("common" when it names none). Scope is left to the caller.
        /// </summary>
        public static void Apply(IdentityProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            var endpoints = IsNamed(provider.Provider, Microsoft)
                ? MicrosoftEndpoints(MicrosoftTenantOf(provider.WellKnownUrl))
                : GoogleEndpoints;

            provider.WellKnownUrl = endpoints.WellKnownUrl;
            provider.AuthorizationUrl = endpoints.AuthorizationUrl;
            provider.TokenUrl = endpoints.TokenUrl;
            provider.UserInfoUrl = endpoints.UserInfoUrl;
        }

        private static string MicrosoftTenantOf(string? discoveryLocation) =>
            TryReadMicrosoftTenant(discoveryLocation, out var tenant) ? tenant : MicrosoftDefaultTenant;

        private static bool IsNamed(string? provider, string name) =>
            string.Equals(provider?.Trim(), name, StringComparison.OrdinalIgnoreCase);
    }
}
