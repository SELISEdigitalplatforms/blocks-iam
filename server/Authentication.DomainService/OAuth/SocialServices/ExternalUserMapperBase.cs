using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    /// <summary>
    /// Shared helpers for <see cref="IExternalUserMapper"/> implementations.
    /// </summary>
    public abstract class ExternalUserMapperBase : IExternalUserMapper
    {
        public abstract string ProviderKey { get; }

        public abstract void Map(JsonElement result, BYOSsoUserData user);

        /// <summary>
        /// Returns the first matching property as a string, or empty string if none match.
        /// </summary>
        protected static string GetString(JsonElement result, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (result.TryGetProperty(key, out var value))
                {
                    return value.ToString();
                }
            }
            return string.Empty;
        }
    }
}
