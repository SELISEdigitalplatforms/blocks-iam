using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    /// <summary>ADFS supports <c>nameid</c> (preferred) or <c>sub</c>, and <c>upn</c> (preferred) or <c>email</c>.</summary>
    public sealed class AdfsExternalUserMapper : ExternalUserMapperBase
    {
        public override string ProviderKey => SocialLogInTypes.Adfs;

        public override void Map(JsonElement result, BYOSsoUserData user)
        {
            user.ExternalProviderUserId = result.TryGetProperty("nameid", out var nameId) ? nameId.ToString()
                : GetString(result, "sub");
            user.Email = result.TryGetProperty("upn", out var upn) ? upn.ToString()
                : GetString(result, "email");
            user.DisplayName = GetString(result, "displayname");
        }
    }
}
