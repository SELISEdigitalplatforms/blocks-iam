using Iam.DomainService.Accounts;
using Iam.DomainService.Configurations;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Iam.DomainService.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Iam.DomainService.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {

        public static void RegisterSharedServices(this IServiceCollection services)
        {
            #region Services
            services.AddSingleton<IUserManagementMutationService, UserManagementMutationService>();
            services.AddSingleton<IUserRepository, UserRepository>();

            services.AddSingleton<IIdentityAccessManagementService, IdentityAccessManagementService>();
            services.AddSingleton<IIdentityAccessManagementRepository, IdentityAccessManagementRepository>();

            services.AddSingleton<IResourceMutationService, ResourceMutationService>();
            services.AddSingleton<IResourceRepository, ResourceRepository>();
            services.AddSingleton<IOrganizationNameResolver, OrganizationNameResolver>();

            services.AddSingleton<IUserManagementQueryService, UserManagementQueryService>();
            services.AddSingleton<IResourceQueryService, ResourceQueryService>();

            services.AddSingleton<IAccountService, AccountService>();
            services.AddSingleton<IIamConfigurationRepository, IamConfigurationRepository>();

            services.AddSingleton<ISignupLinkConfigurationRepository, SignupLinkConfigurationRepository>();
            services.AddSingleton<ISignupLinkConfigurationService, SignupLinkConfigurationService>();
            services.AddSingleton<ISignupLinkRepository, SignupLinkRepository>();
            services.AddSingleton<ISignupLinkGenerationService, SignupLinkGenerationService>();
            services.AddSingleton<IGrantAuthorizationService, GrantAuthorizationService>();
            services.AddHttpContextAccessor();


            #endregion

            #region Validators
            services.AddSingleton<IValidator<BaseAccountRequest>, BaseAccountValidator>();
            services.AddTransient<IValidator<ChangePasswordRequest>, ChangePasswordValidator>();
            services.AddTransient<IValidator<CreateUserRequest>, CreateUserValidator>();
            services.AddTransient<IValidator<UpdateUserRequest>, UpdateUserValidator>();
            services.AddTransient<IValidator<UpdateMyAccountRequest>, UpdateMyAccountValidator>();
            services.AddTransient<IValidator<GetUsersRequest>, GetUsersRequestValidator>();
            services.AddTransient<IValidator<CreatePermissionRequest>, CreatePermissionValidator>();
            services.AddTransient<IValidator<CreateRoleRequest>, RoleValidator>();
            services.AddTransient<IValidator<UpdatePermissionRequest>, UpdatePermissionValidator>();
            services.AddTransient<IValidator<RecoveryUserRequest>, RecoveryUserRequestValidator>();
            services.AddTransient<IValidator<SignupOrganizationInfo>, SignupOrganizationValidator>();
            services.AddTransient<IValidator<CreateSignupLinkConfigurationRequest>, CreateSignupLinkConfigurationValidator>();
            services.AddTransient<IValidator<UpdateSignupLinkConfigurationRequest>, UpdateSignupLinkConfigurationValidator>();
            services.AddTransient<IValidator<QuerySignupLinkConfigurationsRequest>, QuerySignupLinkConfigurationsValidator>();
            services.AddTransient<IValidator<GenerateSignupLinkRequest>, GenerateSignupLinkValidator>();
            services.AddTransient<IValidator<QuerySignupLinksRequest>, QuerySignupLinksValidator>();
            services.AddTransient<IValidator<RevokeSignupLinksByConfigurationRequest>, RevokeSignupLinksByConfigurationValidator>();
            #endregion

        }

    }
}
