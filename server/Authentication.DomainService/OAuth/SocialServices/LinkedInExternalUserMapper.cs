using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    public sealed class LinkedInExternalUserMapper : ExternalUserMapperBase
    {
        public override string ProviderKey => SocialLogInTypes.LinkedIn;

        public override void Map(JsonElement result, BYOSsoUserData user)
        {
            user.ExternalProviderUserId = GetString(result, "id");
            user.FirstName = GetString(result, "localizedFirstName");
            user.LastName = GetString(result, "localizedLastName");
            user.DisplayName = $"{user.FirstName} {user.LastName}".Trim();
            user.Email = GetString(result, "email");
            user.ProfileImageUrl = GetString(result, "profilePicture");
        }
    }
}
