namespace Authentication.DomainService.OAuth
{
    internal interface ISocialLogInService
    {
        Task<SocialCallbackResult> HandleSocialLoginCallback(StateInfo stateInfo);
        Task<IExternalUserData> HandleSocialLogin(StateInfo stateInfo);
    }
}
