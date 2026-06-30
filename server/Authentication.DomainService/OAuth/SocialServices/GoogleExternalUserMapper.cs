using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    public sealed class GoogleExternalUserMapper : ExternalUserMapperBase
    {
        public override string ProviderKey => SocialLogInTypes.Google;

        public override void Map(JsonElement result, BYOSsoUserData user)
        {
            user.ExternalProviderUserId = GetString(result, "sub");
            user.Email = GetString(result, "email");
            user.DisplayName = GetString(result, "name");
            user.FirstName = GetString(result, "given_name");
            user.LastName = GetString(result, "family_name");
            user.ProfileImageUrl = GetString(result, "picture");
        }
    }
}
