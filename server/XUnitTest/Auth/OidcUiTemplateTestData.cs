using Authentication.DomainService.Authentication;
using Authentication.DomainService.Shared.RequestModel;

namespace XUnitTest.Auth
{
    internal static class OidcUiTemplateTestData
    {
        public static SaveOidcUiTemplateRequest ValidRequest()
        {
            var template = IdpService.CreateDefaultOidcUiTemplate();
            return new SaveOidcUiTemplateRequest
            {
                Branding = template.Branding,
                Theme = template.Theme,
                Pages = template.Pages
            };
        }
    }
}
