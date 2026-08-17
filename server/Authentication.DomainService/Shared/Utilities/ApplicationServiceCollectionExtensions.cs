using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.OAuth.SocialServices;
using Authentication.DomainService.Oidc.Contracts;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Security.Utilities;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Authentication.DomainService.Shared.RequestModel;
using Authentication.DomainService.Shared.Services;
using Blocks.Extension.DependencyInjection;
using DomainService.Storage;
using FluentValidation;
using Iam.DomainService.Accounts;
using Iam.DomainService.Activity.Services;
using Iam.DomainService.Configurations;
using Iam.DomainService.Resources;
using Iam.DomainService.Resources.TenantPropagation;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Services;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Mfa.DomainService.Validators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Storage.DomainService.Shared.Services;
using Storage.DomainService.Storage;
using Storage.DomainService.Storage.Validators;


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

            // Satisfies Iam.DomainService's account-action email builders, which need the
            // tenant's default OIDC client but cannot reference this assembly.
            serviceCollection.AddSingleton<IDefaultOidcClientResolver, DefaultOidcClientResolver>();

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

            // RFC 8628 Device Authorization Grant
            serviceCollection.AddOptions<DeviceFlowOptions>()
                .Configure<IConfiguration>((options, configuration) =>
                    configuration.GetSection(DeviceFlowOptions.SectionName).Bind(options))
                .ValidateOnStart();
            serviceCollection.AddSingleton<DeviceCodeGenerator>();
            serviceCollection.AddSingleton<IDeviceAuthorizationRepository, DeviceAuthorizationRepository>();
            serviceCollection.AddSingleton<IDeviceAuthorizationService, DeviceAuthorizationService>();
            serviceCollection.AddSingleton<IOidcTokenMintService, OidcTokenMintService>();
            serviceCollection.AddSingleton<DeviceAuthorizationEndpoint>();
            serviceCollection.AddSingleton<DeviceVerificationService>();
            serviceCollection.AddSingleton<DeviceCodeExchangeService>();
            serviceCollection.AddHostedService<DeviceCleanupWorker>();
            serviceCollection.AddHostedService<BlacklistIndexWorker>();
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
            serviceCollection.AddTransient<IValidator<SaveOIDCClientRequest>, SaveOIDCClientRequestValidator>();
            serviceCollection.AddTransient<IValidator<SaveIdentityProviderRequest>, SaveIdentityProviderRequestValidator>();
            serviceCollection.AddTransient<IValidator<UpdateIdentityProviderRequest>, UpdateIdentityProviderRequestValidator>();

            #endregion

            #region IAM
            serviceCollection.AddSingleton<IUserManagementMutationService, UserManagementMutationService>();
            serviceCollection.AddSingleton<IUserRepository, UserRepository>();

            serviceCollection.AddSingleton<IIdentityAccessManagementService, IdentityAccessManagementService>();
            serviceCollection.AddSingleton<IIdentityAccessManagementRepository, IdentityAccessManagementRepository>();

            serviceCollection.AddSingleton<IResourceMutationService, ResourceMutationService>();
            serviceCollection.AddSingleton<IResourceRepository, ResourceRepository>();

            serviceCollection.AddSingleton<ITenantPermissionPropagator, TenantPermissionPropagator>();
            // ITenantEnumeration / IMongoDatabase (root) must be registered by every host that
            // calls RegisterAllServices — TenantPermissionPropagator depends on both. See
            // Api/Program.cs and Worker/Program.cs for the per-host registrations.

            serviceCollection.AddSingleton<IUserManagementQueryService, UserManagementQueryService>();
            serviceCollection.AddSingleton<IResourceQueryService, ResourceQueryService>();

            serviceCollection.AddSingleton<IAccountService, AccountService>();
            serviceCollection.AddSingleton<IIamConfigurationRepository, IamConfigurationRepository>();

            serviceCollection.AddSingleton<IUserActivityRepository, UserActivityRepository>();
            serviceCollection.AddSingleton<IUserActivityDispatcher, UserActivityDispatcher>();
            serviceCollection.AddSingleton<IUserActivityQueryService, UserActivityQueryService>();

            serviceCollection.RegisterSecurityServices();

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
            serviceCollection.AddSingleton<IMfaBackupCodeService, MfaBackupCodeService>();
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
            serviceCollection.AddSingleton<IRefreshSessionResolver, RefreshSessionResolver>();
            serviceCollection.AddSingleton<IImpersonationFlowHelper, ImpersonationFlowHelper>();

            // Drivers
            serviceCollection.AddSingleton<DmsArtifactBuilderFactory>();
            serviceCollection.AddTransient<IValidator<UpdateFileRequest>, UpdateFileRequestValidator>();
            serviceCollection.AddTransient<AwsS3CompatibleStorageService>();
            serviceCollection.AddSingleton<FileArtifactBuilder>();
            serviceCollection.AddSingleton<FolderArtifactBuilder>();

            serviceCollection.RegisterBlocksStorageServices();
        }
    }
}
