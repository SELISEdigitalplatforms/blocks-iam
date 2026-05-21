using Authentication.DomainService.OAuth.RequestModel;

namespace Authentication.DomainService.OAuth
{
    internal interface ISocialLogInService
    {
        Task<(string, bool)> GetProviderLogInUriAsync(GetSocialLogInEndPointRequest loginData);
        Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo);
        Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo);
    }
}
