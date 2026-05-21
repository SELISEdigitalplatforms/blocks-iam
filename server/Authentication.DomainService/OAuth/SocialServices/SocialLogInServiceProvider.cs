using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.SocialServices;
using Microsoft.Extensions.DependencyInjection;

namespace Authentication.DomainService.OAuth
{
    public class SocialLogInServiceProvider : ISocialLogInServiceProvider
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
        public async Task<GetSocialLogInEndPointResponse> GetSocialLogInEndPointAsync(GetSocialLogInEndPointRequest request)
        {
            var service = _socialLogIns.ContainsKey(request.ClientId) ? _socialLogIns[request.Provider.ToLower()] : _defaultService;
            var (link, response) = await service.GetProviderLogInUriAsync(request);
            return new GetSocialLogInEndPointResponse
            {
                ProviderUrl = link,
                IsAResponse = response,
                Error = string.IsNullOrWhiteSpace(link) ? $"Credential not found for provider {request.Provider} and audience {request.Audience}" : string.Empty
            };
        }

        public async Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo)
        {
            var service = _socialLogIns.ContainsKey(stateInfo.ClientId) ? _socialLogIns[stateInfo.Provider.ToLower()] : _defaultService;
            return await service.HandleSocialLoginCallback(stateInfo);
        }

        public async Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo)
        {
            var callbackResult = await HandleSocialLoginCallback(stateInfo);
            return callbackResult.ExternalUserData;
        }
    }

}
