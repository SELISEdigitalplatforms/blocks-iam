using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    /// <summary>
    /// Dispatches an external user-info payload to the registered <see cref="IExternalUserMapper"/>
    /// matching the provider name. Falls back to <see cref="GenericOidcExternalUserMapper"/>
    /// when no specific mapper is registered.
    /// </summary>
    public interface IExternalUserMapperRegistry
    {
        /// <summary>
        /// Populate <paramref name="user"/> from <paramref name="result"/> using the mapper
        /// registered for <paramref name="provider"/> (case-insensitive), or the generic fallback.
        /// </summary>
        void Map(string provider, JsonElement result, BYOSsoUserData user);
    }
}
