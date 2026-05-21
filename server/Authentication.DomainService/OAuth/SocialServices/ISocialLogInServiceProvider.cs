namespace Authentication.DomainService.OAuth
{
    public interface ISocialLogInServiceProvider
    {
        Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo);
        Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo);
    }
}
