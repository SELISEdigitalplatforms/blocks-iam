using Authentication.DomainService.Entities;
using Authentication.DomainService.Oidc.Repositories;
using Blocks.CaptchaDriver;
using Blocks.Genesis;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Facade combining captcha service + configuration. Replaces 2 separate deps (S107).
    /// </summary>
    public interface ICaptchaEvaluator
    {
        Task<CaptchaConfiguration?> GetConfigurationAsync();
        Task<object> VerifyAsync(string captchaCode, string configurationName);
    }
}
