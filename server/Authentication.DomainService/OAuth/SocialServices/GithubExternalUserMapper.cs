using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    public sealed class GithubExternalUserMapper : ExternalUserMapperBase
    {
        public override string ProviderKey => SocialLogInTypes.Github;

        public override void Map(JsonElement result, BYOSsoUserData user)
        {
            user.ExternalProviderUserId = GetString(result, "id");
            user.Email = GetString(result, "email");
            user.DisplayName = GetString(result, "login");
            user.ProfileImageUrl = GetString(result, "avatar_url");
        }
    }
}
