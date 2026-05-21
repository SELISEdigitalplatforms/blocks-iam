using Authentication.DomainService.OAuth.RequestModel;

namespace Authentication.DomainService.OAuth
{
    public interface ISocialLogInServiceProvider
    {
        Task<GetSocialLogInEndPointResponse> GetSocialLogInEndPointAsync(GetSocialLogInEndPointRequest request);
        Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo);
        Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo);
    }
}
