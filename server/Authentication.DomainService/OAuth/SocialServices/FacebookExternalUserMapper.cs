using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    public sealed class FacebookExternalUserMapper : ExternalUserMapperBase
    {
        public override string ProviderKey => SocialLogInTypes.FaceBook;

        public override void Map(JsonElement result, BYOSsoUserData user)
        {
            user.ExternalProviderUserId = GetString(result, "id");
            user.Email = GetString(result, "email");
            user.DisplayName = GetString(result, "name");
            user.ProfileImageUrl = GetString(result, "picture");
        }
    }
}
