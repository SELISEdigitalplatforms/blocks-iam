using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    public sealed class KeycloakExternalUserMapper : ExternalUserMapperBase
    {
        public override string ProviderKey => SocialLogInTypes.Keycloak;

        public override void Map(JsonElement result, BYOSsoUserData user)
        {
            user.ExternalProviderUserId = GetString(result, "sub");
            user.Email = GetString(result, "email");
            user.DisplayName = GetString(result, "name", "preferred_username");
        }
    }
}
