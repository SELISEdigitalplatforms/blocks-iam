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

public sealed class SignupLinkRedemptionCollaborators
{
    public IOidcClientRegistrationLookup Oidc { get; }
    public IUserManagementMutationService UserMutation { get; }
    public ICacheClient Cache { get; }
    public ITenants Tenants { get; }
    public IConfiguration Configuration { get; }
    public IMfaChallengeIssuer Mfa { get; }
    public ILogger<SignupLinkRedemptionCollaborators> Logger { get; }

    public SignupLinkRedemptionCollaborators(
        IOidcClientRegistrationLookup oidc,
        IUserManagementMutationService userMutation,
        ICacheClient cache,
        ITenants tenants,
        IConfiguration configuration,
        IMfaChallengeIssuer mfa,
        ILogger<SignupLinkRedemptionCollaborators> logger)
    {
        Oidc = oidc;
        UserMutation = userMutation;
        Cache = cache;
        Tenants = tenants;
        Configuration = configuration;
        Mfa = mfa;
        Logger = logger;
    }
}
