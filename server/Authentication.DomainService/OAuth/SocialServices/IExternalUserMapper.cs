using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    /// <summary>
    /// Maps an external provider's user-info JSON payload into a <see cref="BYOSsoUserData"/>.
    /// One implementation per IdP. Dispatched by <see cref="IExternalUserMapperRegistry"/>.
    /// </summary>
    public interface IExternalUserMapper
    {
        /// <summary>Provider key this mapper handles (case-insensitive). Matches <see cref="SocialLogInTypes"/>.</summary>
        string ProviderKey { get; }

        /// <summary>Populate <paramref name="user"/> from the raw <paramref name="result"/> JSON element.</summary>
        void Map(JsonElement result, BYOSsoUserData user);
    }
}
