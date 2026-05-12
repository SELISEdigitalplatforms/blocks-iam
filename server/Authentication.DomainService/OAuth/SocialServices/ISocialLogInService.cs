using Authentication.DomainService.OAuth.RequestModel;

namespace Authentication.DomainService.OAuth
{
    internal interface ISocialLogInService
    {
        Task<(string, bool)> GetProviderLogInUriAsync(GetSocialLogInEndPointRequest loginData);
        Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo);
    }
}
