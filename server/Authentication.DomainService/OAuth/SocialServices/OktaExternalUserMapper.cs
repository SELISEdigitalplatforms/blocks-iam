using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    public sealed class OktaExternalUserMapper : ExternalUserMapperBase
    {
        public override string ProviderKey => SocialLogInTypes.Okta;

        public override void Map(JsonElement result, BYOSsoUserData user)
        {
            user.ExternalProviderUserId = GetString(result, "sub");
            user.Email = GetString(result, "email");
            user.DisplayName = GetString(result, "name");
            user.FirstName = GetString(result, "given_name");
            user.LastName = GetString(result, "family_name");
        }
    }
}
