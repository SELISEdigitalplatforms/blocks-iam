using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.CaptchaDriver;
using Iam.DomainService.Entities;

namespace Authentication.DomainService.Authentication
{
    public sealed class AuthStrategy : IAuthStrategy
    {
        private readonly PasswordAuthenticationService _passwordAuthenticationService;
        private readonly MfaAuthorizationService _mfaAuthorizationService;
        private readonly SocialAuthorizationService _socialAuthorizationService;

        public AuthStrategy(
            PasswordAuthenticationService passwordAuthenticationService,
            MfaAuthorizationService mfaAuthorizationService,
            SocialAuthorizationService socialAuthorizationService)
        {
            _passwordAuthenticationService = passwordAuthenticationService;
            _mfaAuthorizationService = mfaAuthorizationService;
            _socialAuthorizationService = socialAuthorizationService;
        }

        public Task<TokenResponse> AuthenticatePasswordAsync(TokenRequest tokenRequest, IdentityConfiguration authConfiguration)
            => _passwordAuthenticationService.AuthenticateAsync(tokenRequest, authConfiguration);

        public Task<TokenResponse> AuthenticateMfaAsync(TokenRequest tokenRequest, IdentityConfiguration authConfiguration, User user)
            => _mfaAuthorizationService.AuthenticateAsync(tokenRequest, authConfiguration, user);

        public Task<TokenResponse> AuthenticateSocialAsync(TokenRequest tokenRequest, IdentityConfiguration authConfiguration)
            => _socialAuthorizationService.AuthenticateAsync(tokenRequest, authConfiguration);
    }
}
