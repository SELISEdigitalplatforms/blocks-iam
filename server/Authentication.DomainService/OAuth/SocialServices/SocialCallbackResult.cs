namespace Authentication.DomainService.OAuth
{
    public class SocialCallbackResult
    {
        public IExternalUserData ExternalUserData { get; set; } = new BYOSsoUserData();
        public string? AccessToken { get; set; }
        public string? IdToken { get; set; }
        public string? RefreshToken { get; set; }
    }
}
