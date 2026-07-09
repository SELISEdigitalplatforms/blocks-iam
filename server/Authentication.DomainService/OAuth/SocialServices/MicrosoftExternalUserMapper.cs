using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    /// <summary>Maps Microsoft / AzureAD / WindowsLive user-info payloads. AzureAD also supports <c>oid</c> fallback.</summary>
    public sealed class MicrosoftExternalUserMapper : ExternalUserMapperBase
    {
        public override string ProviderKey => SocialLogInTypes.Microsoft;

        public override void Map(JsonElement result, BYOSsoUserData user)
        {
            user.ExternalProviderUserId = result.TryGetProperty("oid", out var oid) ? oid.ToString()
                : GetString(result, "sub");
            user.Email = GetString(result, "preferred_username", "email");
            user.DisplayName = GetString(result, "name");
            user.FirstName = GetString(result, "given_name");
            user.LastName = GetString(result, "family_name");
        }
    }
}
