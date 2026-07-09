using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    /// <summary>Default fallback mapper for unknown / custom OIDC providers (Auth0, WindowsLive, etc.).</summary>
    public sealed class GenericOidcExternalUserMapper : ExternalUserMapperBase
    {
        public override string ProviderKey => "__generic__";

        public override void Map(JsonElement result, BYOSsoUserData user)
        {
            user.ExternalProviderUserId = GetString(result, "sub");
            user.Email = GetString(result, "email");
            user.DisplayName = GetString(result, "name");
            user.FirstName = GetString(result, "given_name");
            user.LastName = GetString(result, "family_name");
            user.ProfileImageUrl = GetString(result, "picture");
            user.PhoneNumber = GetString(result, "phone_number");
        }
    }
}
