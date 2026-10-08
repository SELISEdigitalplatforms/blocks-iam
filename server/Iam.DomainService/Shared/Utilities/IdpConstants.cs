using Blocks.Genesis;

namespace Iam.DomainService.Utilities
{
    public static class IdpConstants
    {
        #region Queues

        public const string AuthenticationQueue = "blocks_authentication_listener";
        public const string IamUserQueue = "blocks_iam_listener_user";
        public const string IamResourceQueue = "blocks_iam_listener_resource";
        public const string IamPermissionQueue = "blocks_iam_listener_permission";
        public const string IamOrgQueue = "blocks_iam_org_listener";

        // A dedicated queue rather than IamUserQueue: a 5000-user batch on the shared user queue
        // would head-of-line-block ordinary per-user mutation events behind it.
        public const string IamBulkRoleQueue = "blocks_iam_listener_bulk_role";
        public const string MailQueue = "blocks_email_listener";
        public const string MfaQueueName = "blocks_mfa_listener";
        public const string UserActivityQueue = "blocks_user_activity_listener";

        #endregion

        #region Mail Purposes

        // Informational only: the change has already been applied when these go out, so a
        // tenant with no template for either purpose simply sends nothing.
        public const string OrganizationMemberAddedMailPurpose = "organization_member_added";
        public const string OrganizationMemberRemovedMailPurpose = "organization_member_removed";

        #endregion

        #region Cookies

        public const string RefreshTokenCookieName = "rt";
        public const string IdpSessionCookieName = "idp_session_id";

        public static string BuildIdpSessionCookieKey(string? tenantId)
        {
            return string.IsNullOrWhiteSpace(tenantId)
                ? IdpSessionCookieName
                : $"{IdpSessionCookieName}_{tenantId}";
        }

        public const string LinkSessionCookieName = "blocks-link-session";

        public static string BuildLinkSessionCookieKey(string? tenantId)
        {
            return string.IsNullOrWhiteSpace(tenantId)
                ? LinkSessionCookieName
                : $"{LinkSessionCookieName}-{tenantId}";
        }

        #endregion

        #region Providers / Protocols

        public const string BlocksProviderName = "blocks-iam";
        public const string BlocksProviderType = "blocks";
        public const string BlocksOidcProviderType = "blocks-oidc";
        public const string OidcProtocol = "oidc";

        #endregion


        #region Organizations

        public const string DefaultOrganizationId = "default";

        /// <summary>
        /// The organization scope of a caller who is authenticated but belongs to no organization.
        /// <para>
        /// A sentinel rather than an absent or empty claim on purpose: a blank organization is
        /// collapsed back to <see cref="DefaultOrganizationId"/> in more than one place -- Genesis's
        /// endpoint authorization handler and the OIDC refresh path both do it -- which would hand
        /// the tenant-wide scope, the most privileged one there is, to exactly the callers who are
        /// meant to hold none. A non-empty value cannot be collapsed that way.
        /// </para>
        /// </summary>
        public const string NoOrganizationId = "no-org";

        #endregion

        #region Severity

        public const string SeverityInfo = "INFO";
        public const string SeverityWarn = "WARN";
        public const string SeverityError = "ERROR";
        public const string SeverityCritical = "CRITICAL";

        #endregion

        #region Status

        public const string StatusSuccess = "success";
        public const string StatusFailure = "failure";
        public const string StatusSent = "sent";
        public const string StatusDelivered = "delivered";

        #endregion

        #region PKCE / Scopes

        public const string PkceMethodS256 = "S256";
        public const string OpenIdProfileEmailScope = "openid profile email";

        #endregion

        #region Initiate flow

        // Value of the `flow` query parameter on /api/idp/initiate that asks for a link to
        // the signup page instead of an authorize URL. Any other value (including none)
        // keeps the login behaviour.
        public const string InitiateFlowSignup = "signup";

        #endregion

        #region Session timeouts

        public const int MaxIdpSessionHours = 168;
        public const int DefaultIdpSessionIdleHours = 24;
        public const int DefaultIdpSessionAbsoluteHours = 5;

        /// <summary>Restricted signup-link session lifetime (minutes). Independent of IdP session timeouts.</summary>
        public const int LinkSessionLifetimeMinutes = 30;

        #endregion

        #region Backchannel retry / timeout

        public const int BackchannelRetryBackoffMilliseconds = 250;
        public const int BackchannelLogoutMaxAttempts = 3;
        public const int BackchannelTimeoutSeconds = 100;

        #endregion

        #region Cache TTL (seconds)

        public const int SocialAuthorizationUrlCacheTtlSeconds = 300;
        public const int OidcAuthorizationCodeCacheTtlSeconds = 600;
        public const int OidcStateCacheTtlSeconds = 300;
        public const int IdpFlowCacheTtlSeconds = 600;

        #endregion

        #region Signup link password step

        /// <summary>signup_link_auth:{redemptionId} -> pending password step context.</summary>
        public const string SignupLinkAuthCachePrefix = "signup_link_auth:";

        /// <summary>SETNX key that makes a redemption id single use.</summary>
        public const string SignupLinkAuthClaimCachePrefix = "signup_link_auth_claim:";

        /// <summary>SETNX key that makes a pending-grant MFA completion single use.</summary>
        public const string SignupLinkMfaClaimCachePrefix = "signup_link_mfa_claim:";

        /// <summary>Wrong passwords allowed on one redemption id before it is discarded.</summary>
        public const int SignupLinkAuthMaxAttempts = 5;

        #endregion

        #region Cookies TTL

        public const int IdpSessionCookieTtlDays = 30;

        #endregion

        #region Token lifetime

        public const int MinAccessTokenLifetimeSeconds = 60;
        public const int SecondsPerMinute = 60;
        public const int MinTokenLifetimeMinutes = 1;

        #endregion

        #region Outbound HTTP

        public const int OutboundRequestLocalhostTimeoutMinutes = 5;

        #endregion

        #region URIs

        public const string AppleAuthUrl = "https://appleid.apple.com";
        public const string GithubUserEmailsUrl = "https://api.github.com/user/emails";
        public const string FallbackIssuer = "https://localhost:5000";
        public const string ProtectedApiAudience = "api://blocks-protected-api";

        #endregion

        #region IAM routes

        public const string OidcActivateRoute = "oidc/activate/";
        public const string OidcRecoverRoute = "oidc/recover/";

        #endregion

        #region Cache prefix

        public const string TenantTokenPublicCertificateCachePrefix = "tetocertpublic::";

        #endregion

        #region Message configuration

        private const string DefaultProvider = "azure";
        private const string RabbitMqProvider = "rabbitmq";

        public static MessageConfiguration GetMessageConfiguration(string messageConnectionString)
        {
            var provider = GetProvider(messageConnectionString);

            return provider switch
            {
                RabbitMqProvider => CreateRabbitMqConfiguration(),
                _ => CreateAzureServiceBusConfiguration()
            };
        }

        private static string GetProvider(string messageConnectionString)
        {
            if (Uri.TryCreate(messageConnectionString, UriKind.Absolute, out var uri) &&
                (uri.Scheme.Equals("amqp", StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals("amqps", StringComparison.OrdinalIgnoreCase)))
            {
                return RabbitMqProvider;
            }
            return DefaultProvider;
        }

        private static MessageConfiguration CreateRabbitMqConfiguration()
        {
            return new MessageConfiguration
            {
                RabbitMqConfiguration = new RabbitMqConfiguration
                {
                    ConsumerSubscriptions = [ConsumerSubscription.BindToQueue(AuthenticationQueue),
                                             ConsumerSubscription.BindToQueue(IamUserQueue),
                                             ConsumerSubscription.BindToQueue(IamBulkRoleQueue),
                                             ConsumerSubscription.BindToQueue(IamResourceQueue),
                                             ConsumerSubscription.BindToQueue(MfaQueueName),
                                             ConsumerSubscription.BindToQueue(IamOrgQueue),
                                             ConsumerSubscription.BindToQueue(IamPermissionQueue),
                                             ConsumerSubscription.BindToQueue(UserActivityQueue)],
                }
            };
        }

        private static MessageConfiguration CreateAzureServiceBusConfiguration()
        {
            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [AuthenticationQueue, IamUserQueue, IamBulkRoleQueue, IamResourceQueue, MfaQueueName, IamOrgQueue, IamPermissionQueue, UserActivityQueue],
                    Topics = []
                }
            };
        }

        #endregion
    }
}