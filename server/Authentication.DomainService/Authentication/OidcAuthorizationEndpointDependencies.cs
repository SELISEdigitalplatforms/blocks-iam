using Authentication.DomainService.Oidc.Repositories;
using Authentication.DomainService.Oidc.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Resources;
using Iam.DomainService.Users;
using Idp.DomainService.Oidc.Services;
namespace Authentication.DomainService.Authentication
{
    public sealed class OidcAuthorizationSessionStores
    {
        public IAuthorizationCodeRepository AuthCodes { get; }
        public IIdpSessionRepository Sessions { get; }
        public IIdpSessionService SessionService { get; }
        public IPkceService Pkce { get; }
        public ILinkSessionRepository LinkSessions { get; }

        public OidcAuthorizationSessionStores(
            IAuthorizationCodeRepository authCodes,
            IIdpSessionRepository sessions,
            IIdpSessionService sessionService,
            IPkceService pkce,
            ILinkSessionRepository linkSessions)
        {
            AuthCodes = authCodes;
            Sessions = sessions;
            SessionService = sessionService;
            Pkce = pkce;
            LinkSessions = linkSessions;
        }
    }

    public sealed class OidcAuthorizationIdentityStores
    {
        public IUserRepository Users { get; }
        public IAuthenticationRepository Authentication { get; }
        public IAuthenticationService AuthenticationService { get; }
        public ITenants Tenants { get; }
        public ICacheClient Cache { get; }
        public IResourceRepository Resources { get; }

        public OidcAuthorizationIdentityStores(
            IUserRepository users,
            IAuthenticationRepository authentication,
            IAuthenticationService authenticationService,
            ITenants tenants,
            ICacheClient cache,
            IResourceRepository resources)
        {
            Users = users;
            Authentication = authentication;
            AuthenticationService = authenticationService;
            Tenants = tenants;
            Cache = cache;
            Resources = resources;
        }
    }
}
