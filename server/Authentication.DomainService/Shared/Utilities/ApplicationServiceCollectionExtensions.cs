using Authentication.DomainService.OAuth.SocialServices;
using Blocks.Extension.DependencyInjection;
using Idp.DomainService.Oidc.Services;
using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using FluentValidation;
using Iam.DomainService.Accounts;
using Iam.DomainService.Activities;
using Iam.DomainService.Configurations;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Mfa.DomainService.Validators;
using Microsoft.Extensions.DependencyInjection;
using Authentication.DomainService.Shared.Services;

namespace Authentication.DomainService.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void RegisterAllServices(this IServiceCollection serviceCollection)
        {
            
            #region Authentication
            serviceCollection.AddHttpClient(OidcDiscoveryClient.HttpClientName);
            serviceCollection.AddSingleton<OidcDiscoveryClient>();
            serviceCollection.AddSingleton<IAuthenticationDomainService, AuthenticationDomainService>();
            serviceCollection.AddSingleton<IAuthenticationRepository, AuthenticationRepository>();

            serviceCollection.AddSingleton<IOAuthJwtAccessTokenManager, OAuthJwtAccessTokenManager>();
            serviceCollection.AddSingleton<IJwtAccessTokenProvider, JwtAccessTokenProvider>();

            serviceCollection.AddSingleton<IAuthenticationService, AuthenticationService>();
            serviceCollection.AddSingleton<IAuthenticationConfigurationService, AuthenticationConfigurationService>();
            serviceCollection.AddSingleton<IAuthenticationFlowService, AuthenticationFlowService>();
            serviceCollection.AddSingleton<IAuthorizationFlowService, AuthorizationFlowService>();
            serviceCollection.AddSingleton<IIdpService, IdpService>();

            serviceCollection.AddSingleton<OidcSigningKeyMaterial>();
            serviceCollection.AddSingleton<ITokenGenerationService, TokenGenerationService>();
            serviceCollection.AddSingleton<IPkceService, PkceService>();
            serviceCollection.AddSingleton<IDiscoveryService, DiscoveryService>();
            serviceCollection.AddSingleton<IJwksService, JwksService>();
            serviceCollection.AddSingleton<IOidcCallbackHandler, OidcCallbackHandler>();

            serviceCollection.AddSingleton<IAuthorizationCodeRepository, AuthorizationCodeRepository>();
            serviceCollection.AddSingleton<IRefreshTokenRepository, RefreshTokenRepository>();
            serviceCollection.AddSingleton<IIdpSessionRepository, IdpSessionRepository>();
            serviceCollection.AddSingleton<IAuditLogRepository, AuditLogRepository>();
            serviceCollection.AddSingleton<ITokenRevocationRepository, TokenRevocationRepository>();
            serviceCollection.AddSingleton<ITokenRevocationService, TokenRevocationService>();
            serviceCollection.AddSingleton<IIdpSessionService, IdpSessionService>();

            serviceCollection.AddSingleton<PasswordAuthenticationService>();
            serviceCollection.AddSingleton<MfaAuthorizationService>();
            serviceCollection.AddSingleton<IMfaPolicyService, MfaPolicyService>();
            serviceCollection.AddSingleton<IMfaAuditService, MfaAuditService>();
            serviceCollection.AddSingleton<RefreshTokenAuthenticationService>();
            serviceCollection.AddSingleton<SocialAuthorizationService>();
            serviceCollection.AddSingleton<BYOSsoAuthorizationService>();
            serviceCollection.AddSingleton<BiometricAuthorizationService>();
            serviceCollection.AddSingleton<ClientCredentialAuthorizationService>();
            serviceCollection.AddSingleton<ClientUserCodeAuthorizationService>();
            serviceCollection.AddSingleton<SSOConsentAuthenticationService>();
            serviceCollection.AddSingleton<IAuthorizationClaimsResolver, AuthorizationClaimsResolver>();
            serviceCollection.AddSingleton<ClientCredentialsTokenIssuer>();

            // Authorization flow split: lean orchestrator delegates to focused services.
            serviceCollection.AddSingleton<PasswordHasher>();
            serviceCollection.AddSingleton<OidcLoginAuditWriter>();
            serviceCollection.AddSingleton<OidcCaptchaEvaluator>();
            serviceCollection.AddSingleton<OidcLoginOrchestrator>();
            serviceCollection.AddSingleton<OidcAuthorizationEndpoint>();
            serviceCollection.AddSingleton<AuthorizationCodeExchangeService>();
            serviceCollection.AddSingleton<OidcRefreshTokenService>();
            serviceCollection.AddSingleton<OidcTokenEndpoint>();

            serviceCollection.AddSingleton<ICertificateProviderFactory, CertificateProviderFactory>();
            serviceCollection.AddSingleton<ISocialLogInServiceProvider, SocialLogInServiceProvider>();
            serviceCollection.AddSingleton<IdpTokenExchangeClient>();
            serviceCollection.AddSingleton<IAuthSessionFacade, AuthSessionFacade>();
            serviceCollection.AddSingleton<IAuthStrategy, AuthStrategy>();
            serviceCollection.AddSingleton<ICaptchaEvaluator, CaptchaEvaluator>();
            serviceCollection.AddSingleton<ITokenRefresher, TokenRefresher>();
            serviceCollection.AddSingleton<IMfaChallengeIssuer, MfaChallengeIssuer>();

            serviceCollection.AddSingleton<GoogleLogInService>();
            serviceCollection.AddSingleton<MicrosoftLogInService>();
            serviceCollection.AddSingleton<BYOSsoLogInService>();
            serviceCollection.AddSingleton<GithubLogInService>();
            serviceCollection.AddSingleton<LinkedinLogInService>();
            serviceCollection.AddSingleton<TwitterLogInService>();
            serviceCollection.AddSingleton<AppleLogInService>();
            serviceCollection.AddSingleton<FaceBookLogInService>();

            // BYOSso external-user mappers (strategy pattern)
            serviceCollection.AddSingleton<IExternalUserMapper, MicrosoftExternalUserMapper>();
            serviceCollection.AddSingleton<IExternalUserMapper, OktaExternalUserMapper>();
            serviceCollection.AddSingleton<IExternalUserMapper, GoogleExternalUserMapper>();
            serviceCollection.AddSingleton<IExternalUserMapper, GithubExternalUserMapper>();
            serviceCollection.AddSingleton<IExternalUserMapper, FacebookExternalUserMapper>();
            serviceCollection.AddSingleton<IExternalUserMapper, LinkedInExternalUserMapper>();
            serviceCollection.AddSingleton<IExternalUserMapper, KeycloakExternalUserMapper>();
            serviceCollection.AddSingleton<IExternalUserMapper, PingExternalUserMapper>();
            serviceCollection.AddSingleton<IExternalUserMapper, AdfsExternalUserMapper>();
            serviceCollection.AddSingleton<IExternalUserMapper, GenericOidcExternalUserMapper>();
            serviceCollection.AddSingleton<IExternalUserMapperRegistry, ExternalUserMapperRegistry>();

            serviceCollection.AddSingleton<IIamConfigurationRepository, IamConfigurationRepository>();
            serviceCollection.AddTransient<IValidator<SaveSsoCredentialRequest>, SaveSsoCredentialRequestValidator>();

            #endregion

            #region IAM
            serviceCollection.AddSingleton<IUserManagementMutationService, UserManagementMutationService>();
            serviceCollection.AddSingleton<IUserRepository, UserRepository>();

            serviceCollection.AddSingleton<IIdentityAccessManagementService, IdentityAccessManagementService>();
            serviceCollection.AddSingleton<IIdentityAccessManagementRepository, IdentityAccessManagementRepository>();

            serviceCollection.AddSingleton<IResourceMutationService, ResourceMutationService>();
            serviceCollection.AddSingleton<IResourceRepository, ResourceRepository>();

            serviceCollection.AddSingleton<IUserManagementQueryService, UserManagementQueryService>();
            serviceCollection.AddSingleton<IResourceQueryService, ResourceQueryService>();

            serviceCollection.AddSingleton<IUserActivityRepository, UserActivityRepository>();
            serviceCollection.AddSingleton<IUserActivityService, UserActivityService>();
            serviceCollection.AddSingleton<IAccountService, AccountService>();
            serviceCollection.AddSingleton<IIamConfigurationRepository, IamConfigurationRepository>();

            //Validators
            serviceCollection.AddSingleton<IValidator<BaseAccountRequest>, BaseAccountValidator>();
            serviceCollection.AddSingleton<IValidator<ChangePasswordRequest>, ChangePasswordValidator>();
            serviceCollection.AddSingleton<IValidator<CreateUserRequest>, CreateUserValidator>();
            serviceCollection.AddSingleton<IValidator<UpdateUserRequest>, UpdateUserValidator>();
            serviceCollection.AddSingleton<IValidator<CreatePermissionRequest>, CreatePermissionValidator>();
            serviceCollection.AddSingleton<IValidator<CreateRoleRequest>, RoleValidator>();
            serviceCollection.AddSingleton<IValidator<UpdatePermissionRequest>, UpdatePermissionValidator>();
            serviceCollection.AddSingleton<IValidator<RecoveryUserRequest>, RecoveryUserRequestValidator>();

            #endregion

            #region MFA
            serviceCollection.AddSingleton<IMfaManagementService, MfaManagementService>();
            serviceCollection.AddSingleton<IOtpServiceFactory, OtpServiceFactory>();
            serviceCollection.AddSingleton<IMfaManagementRepository, MfaManagementRepository>();
            serviceCollection.AddSingleton<IMfaConfigurationService, MfaConfigurationService>();
            serviceCollection.AddSingleton<TotpService>();
            serviceCollection.AddSingleton<EmailOtpService>();
            serviceCollection.AddHttpContextAccessor();

            serviceCollection.AddTransient<IValidator<VerifyOtpRequest>, VerifyOtpRequestValidator>();

            serviceCollection.AddSingleton<MfaAuditService>();
            serviceCollection.AddSingleton<IMfaAuditService>(sp => sp.GetRequiredService<MfaAuditService>());


            serviceCollection.RegisterBlocksMailService();

            #endregion

            serviceCollection.RegisterBlocksCaptchaService();

            serviceCollection.AddSingleton<UnifiedTokenSessionService, UnifiedTokenSessionService>();
            serviceCollection.AddSingleton<IImpersonationFlowHelper, ImpersonationFlowHelper>();
        }
    }
}