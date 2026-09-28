using FluentValidation;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.SignupLinks;

/// <summary>
/// Bundles collaborators for <see cref="SignupLinkGenerationService"/> so the service
/// constructor stays within the DI parameter budget (S107).
/// </summary>
public sealed class SignupLinkGenerationDependencies
{
    public ISignupLinkRepository Links { get; }
    public ISignupLinkConfigurationRepository Configurations { get; }
    public IOidcClientRegistrationLookup Oidc { get; }
    public IGrantAuthorizationService GrantAuthorization { get; }
    public IUserRepository Users { get; }
    public IConfiguration Configuration { get; }
    public IValidator<GenerateSignupLinkRequest> GenerateValidator { get; }
    public IValidator<QuerySignupLinksRequest> QueryValidator { get; }
    public IValidator<RevokeSignupLinksByConfigurationRequest> RevokeByConfigValidator { get; }
    public ILogger<SignupLinkGenerationService> Logger { get; }

    public SignupLinkGenerationDependencies(
        ISignupLinkRepository links,
        ISignupLinkConfigurationRepository configurations,
        IOidcClientRegistrationLookup oidc,
        IGrantAuthorizationService grantAuthorization,
        IUserRepository users,
        IConfiguration configuration,
        IValidator<GenerateSignupLinkRequest> generateValidator,
        IValidator<QuerySignupLinksRequest> queryValidator,
        IValidator<RevokeSignupLinksByConfigurationRequest> revokeByConfigValidator,
        ILogger<SignupLinkGenerationService> logger)
    {
        Links = links;
        Configurations = configurations;
        Oidc = oidc;
        GrantAuthorization = grantAuthorization;
        Users = users;
        Configuration = configuration;
        GenerateValidator = generateValidator;
        QueryValidator = queryValidator;
        RevokeByConfigValidator = revokeByConfigValidator;
        Logger = logger;
    }
}
