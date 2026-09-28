using FluentValidation;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;

namespace Iam.DomainService.SignupLinks;

/// <summary>
/// Persistence and authorization collaborators for <see cref="SignupLinkGenerationService"/>.
/// </summary>
public sealed class SignupLinkGenerationRepositories
{
    public ISignupLinkRepository Links { get; }
    public ISignupLinkConfigurationRepository Configurations { get; }
    public IOidcClientRegistrationLookup Oidc { get; }
    public IGrantAuthorizationService GrantAuthorization { get; }
    public IUserRepository Users { get; }

    public SignupLinkGenerationRepositories(
        ISignupLinkRepository links,
        ISignupLinkConfigurationRepository configurations,
        IOidcClientRegistrationLookup oidc,
        IGrantAuthorizationService grantAuthorization,
        IUserRepository users)
    {
        Links = links;
        Configurations = configurations;
        Oidc = oidc;
        GrantAuthorization = grantAuthorization;
        Users = users;
    }
}

/// <summary>
/// Configuration and FluentValidation collaborators for <see cref="SignupLinkGenerationService"/>.
/// </summary>
public sealed class SignupLinkGenerationValidators
{
    public IConfiguration Configuration { get; }
    public IValidator<GenerateSignupLinkRequest> GenerateValidator { get; }
    public IValidator<QuerySignupLinksRequest> QueryValidator { get; }
    public IValidator<RevokeSignupLinksByConfigurationRequest> RevokeByConfigValidator { get; }

    public SignupLinkGenerationValidators(
        IConfiguration configuration,
        IValidator<GenerateSignupLinkRequest> generateValidator,
        IValidator<QuerySignupLinksRequest> queryValidator,
        IValidator<RevokeSignupLinksByConfigurationRequest> revokeByConfigValidator)
    {
        Configuration = configuration;
        GenerateValidator = generateValidator;
        QueryValidator = queryValidator;
        RevokeByConfigValidator = revokeByConfigValidator;
    }
}
