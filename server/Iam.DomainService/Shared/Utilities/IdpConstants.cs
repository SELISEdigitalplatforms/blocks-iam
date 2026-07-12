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
        public const string MailQueue = "blocks_email_listener";
        public const string MfaQueueName = "blocks_mfa_listener";
        public const string UserActivityQueue = "blocks_user_activity_listener";

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

        #endregion

        #region Providers / Protocols

        public const string BlocksProviderName = "blocks-iam";
        public const string BlocksProviderType = "blocks";
        public const string OidcProtocol = "oidc";

        #endregion


        #region Organizations

        public const string DefaultOrganizationId = "default";

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

        #region Session timeouts

        public const int MaxIdpSessionHours = 168;
        public const int DefaultIdpSessionIdleHours = 24;
        public const int DefaultIdpSessionAbsoluteHours = 5;

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
                    Queues = [AuthenticationQueue, IamUserQueue, IamResourceQueue, MfaQueueName, IamOrgQueue, IamPermissionQueue, UserActivityQueue],
                    Topics = []
                }
            };
        }

        #endregion
    }
}