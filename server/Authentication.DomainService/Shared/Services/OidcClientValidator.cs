using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Idp.DomainService.Oidc.Contracts;

namespace Authentication.DomainService.Shared.Services
{
    /// <summary>
    /// Single source of truth for which OAuth grant types a given <see cref="OidcClientRegistration"/>
    /// may use. The effective grant set is derived from the boolean <see cref="OidcClientRegistration.IsDeviceFlowClient"/>
    /// flag and the standard three-grant set (authorization_code, refresh_token, client_credentials) —
    /// the boolean determines the client's capability mode; no grant-type list is persisted on the client.
    /// </summary>
    public static class OidcClientValidator
    {
        public static bool IsGrantAllowed(OidcClientRegistration? client, string grantType)
        {
            if (client == null || !client.IsActive)
            {
                return false;
            }

            if (string.Equals(grantType, GrantTypes.DeviceCode, StringComparison.OrdinalIgnoreCase))
            {
                return client.IsDeviceFlowClient;
            }

            if (string.Equals(grantType, GrantTypes.AuthCode, StringComparison.OrdinalIgnoreCase))
            {
                return !client.IsDeviceFlowClient;
            }

            // Device clients mint their initial token via the device_code grant, but RFC 8628
            // still expects them to refresh like any other client — refresh_token is not
            // authorization_code and must not be gated on IsDeviceFlowClient.
            if (string.Equals(grantType, GrantTypes.RefreshToken, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(grantType, GrantTypes.ClientCredential, StringComparison.OrdinalIgnoreCase))
            {
                if (client.IsDeviceFlowClient)
                {
                    return false;
                }

                var method = client.TokenEndpointAuthMethod;
                return !string.IsNullOrWhiteSpace(method)
                    && !string.Equals(method, "none", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static bool IsPublicClient(OidcClientRegistration? client, string grantType)
        {
            if (client == null)
            {
                return false;
            }

            if (string.Equals(grantType, GrantTypes.DeviceCode, StringComparison.OrdinalIgnoreCase))
            {
                return client.IsDeviceFlowClient;
            }

            if (string.Equals(grantType, GrantTypes.AuthCode, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(client.ClientType, "public", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static IReadOnlyList<string> ValidateScopes(
            OidcClientRegistration? client,
            string? requestedScope,
            IEnumerable<string> supportedScopes)
        {
            if (client == null)
            {
                return [];
            }

            var allowed = new HashSet<string>(client.AllowedScopes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var supported = new HashSet<string>(supportedScopes, StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(requestedScope))
            {
                return allowed.Intersect(supported).ToList();
            }

            var requested = requestedScope
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var result = new List<string>();
            foreach (var s in requested)
            {
                if (allowed.Contains(s) && supported.Contains(s))
                {
                    result.Add(s);
                }
            }
            return result;
        }
    }

    public static class ScopeConstants
    {
        public static readonly IReadOnlyList<string> Supported = new[]
        {
            "openid",
            "profile",
            "email",
            "offline_access"
        };
    }
}