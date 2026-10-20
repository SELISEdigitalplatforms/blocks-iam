using System.Security.Cryptography;
using System.Text;

namespace Authentication.DomainService.Oidc.Repositories
{
    /// <summary>
    /// Redis keys for short-lived sign-in state. The secret part (code, session id) is always hashed,
    /// so no raw value ever appears in a key. The tenant id is plain (no braces: braces are a Redis
    /// Cluster hash tag).
    /// </summary>
    public static class IamRedisKeys
    {
        public static string AuthorizationCode(string? tenantId, string code) =>
            $"iam:{RequireTenant(tenantId)}:authcode:{Sha256Hex(code)}";

        public static string LinkSession(string? tenantId, string sessionId) =>
            $"iam:{RequireTenant(tenantId)}:linksession:{Sha256Hex(sessionId)}";

        /// <summary>Failed-login state for a username that has no usable account (see UnknownUserLoginGuard).</summary>
        public static string UnknownLogin(string? tenantId, string normalizedUsername) =>
            $"iam:{RequireTenant(tenantId)}:login-unknown:{Sha256Hex(normalizedUsername)}";

        public static string Sha256Hex(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

        private static string RequireTenant(string? tenantId) =>
            string.IsNullOrWhiteSpace(tenantId)
                ? throw new InvalidOperationException("No tenant is available for the Redis key.")
                : tenantId;
    }
}
