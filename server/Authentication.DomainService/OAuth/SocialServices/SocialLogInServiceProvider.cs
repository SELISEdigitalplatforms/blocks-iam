using Authentication.DomainService.OAuth.SocialServices;
using Microsoft.Extensions.DependencyInjection;

namespace Authentication.DomainService.OAuth
{
    public sealed class SocialLogInServiceProvider : ISocialLogInServiceProvider
    {
        private readonly IDictionary<string, ISocialLogInService> _socialLogIns;
        private readonly ISocialLogInService _defaultService;

        public SocialLogInServiceProvider(IServiceProvider serviceProvider)
        {
            _defaultService = serviceProvider.GetService<BYOSsoLogInService>();
            _socialLogIns = new SortedDictionary<string, ISocialLogInService>
            {
                { SocialLogInTypes.Google, serviceProvider.GetService<GoogleLogInService>() },
                { SocialLogInTypes.Microsoft, serviceProvider.GetService<MicrosoftLogInService>() },
                { SocialLogInTypes.Github, serviceProvider.GetService<GithubLogInService>() },
                { SocialLogInTypes.LinkedIn, serviceProvider.GetService<LinkedinLogInService>() },
                { SocialLogInTypes.Twitter, serviceProvider.GetService<TwitterLogInService>() },
                { SocialLogInTypes.Apple, serviceProvider.GetService<AppleLogInService>() },
                { SocialLogInTypes.FaceBook, serviceProvider.GetService<FaceBookLogInService>() }
            };
        }

        public async Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo)
        {
            var service = _socialLogIns.ContainsKey(stateInfo.Provider) ? _socialLogIns[stateInfo.Provider.ToLower()] : _defaultService;
            return await service.HandleSocialLoginCallback(stateInfo);
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var callbackResult = await HandleSocialLoginCallback(stateInfo);
            return callbackResult.ExternalUserData;
        }
    }

}
