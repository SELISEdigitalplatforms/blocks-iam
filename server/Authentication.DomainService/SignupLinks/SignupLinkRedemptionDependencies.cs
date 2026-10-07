using Authentication.DomainService.Authentication;
using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.SignupLinks;

public sealed class SignupLinkRedemptionStores
{
    public ISignupLinkRepository Links { get; }
    public ISignupLinkRedemptionRepository Redemptions { get; }
    public ILinkSessionRepository LinkSessions { get; }
    public IUserRepository Users { get; }
    public IIdentityAccessManagementRepository Iam { get; }

    public SignupLinkRedemptionStores(
        ISignupLinkRepository links,
        ISignupLinkRedemptionRepository redemptions,
        ILinkSessionRepository linkSessions,
        IUserRepository users,
        IIdentityAccessManagementRepository iam)
    {
        Links = links;
        Redemptions = redemptions;
        LinkSessions = linkSessions;
        Users = users;
        Iam = iam;
    }
}

/// <summary>
/// Cross-cutting services the redemption orchestrator uses: cache, tenant lookup,
/// configuration, activity events and its logger.
/// </summary>
public sealed class SignupLinkRedemptionInfrastructure
{
    public ICacheClient Cache { get; }
    public ITenants Tenants { get; }
    public IConfiguration Configuration { get; }

    /// <summary>
    /// Records the activation when a link activates a pending account, as the activation
    /// email does.
    /// </summary>
    public IUserActivityDispatcher UserActivity { get; }

    public ILogger<SignupLinkRedemptionCollaborators> Logger { get; }

    public SignupLinkRedemptionInfrastructure(
        ICacheClient cache,
        ITenants tenants,
        IConfiguration configuration,
        IUserActivityDispatcher userActivity,
        ILogger<SignupLinkRedemptionCollaborators> logger)
    {
        Cache = cache;
        Tenants = tenants;
        Configuration = configuration;
        UserActivity = userActivity;
        Logger = logger;
    }
}

public sealed class SignupLinkRedemptionCollaborators
{
    public IOidcClientRegistrationLookup Oidc { get; }

    /// <summary>
    /// Resolves the Blocks IdentityProvider mirroring a link's OIDC client. The redemption's
    /// authorize URL is completed at <c>/idp/callback</c>, which looks the provider up by name,
    /// so the name written into the flow context has to be the client's real one.
    /// </summary>
    public IAuthenticationRepository Authentication { get; }

    public IUserManagementMutationService UserMutation { get; }
    public IMfaChallengeIssuer Mfa { get; }
    public ISignupLinkEmbeddedTokenIssuer EmbeddedTokens { get; }
    public ICacheClient Cache => _infrastructure.Cache;
    public ITenants Tenants => _infrastructure.Tenants;
    public IConfiguration Configuration => _infrastructure.Configuration;
    public IUserActivityDispatcher UserActivity => _infrastructure.UserActivity;
    public ILogger<SignupLinkRedemptionCollaborators> Logger => _infrastructure.Logger;

    /// <summary>
    /// Checks an existing user's password for the signup-link password step, with the same
    /// lockout and CAPTCHA rules as embedded login.
    /// </summary>
    public IPasswordCredentialVerifier? PasswordVerifier { get; }

    private readonly SignupLinkRedemptionInfrastructure _infrastructure;

    public SignupLinkRedemptionCollaborators(
        IOidcClientRegistrationLookup oidc,
        IAuthenticationRepository authentication,
        IUserManagementMutationService userMutation,
        IMfaChallengeIssuer mfa,
        ISignupLinkEmbeddedTokenIssuer embeddedTokens,
        SignupLinkRedemptionInfrastructure infrastructure,
        IPasswordCredentialVerifier? passwordVerifier = null)
    {
        Oidc = oidc;
        Authentication = authentication;
        UserMutation = userMutation;
        Mfa = mfa;
        EmbeddedTokens = embeddedTokens;
        _infrastructure = infrastructure;
        PasswordVerifier = passwordVerifier;
    }
}
