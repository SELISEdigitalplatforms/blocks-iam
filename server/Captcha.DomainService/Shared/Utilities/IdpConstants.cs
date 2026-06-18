using Blocks.Genesis;
using Captcha.DomainService.Configuration;

namespace Authentication.DomainService.Utilities
{
    public static class IdpConstants
    {
        public const string TenantTokenPublicCertificateCachePrefix = "tetocertpublic::";
        public const string AuthenticationQueue = "blocks_idp_authentication_listener";
        public const string IamQueue = "blocks_idp_iam_listener";
        public const string IamOrgQueue = "blocks_idp_iam_org_listener";
        public const string MailQueue = "blocks_email_listener";
        public const string MfaQueueName = "blocks_idp_mfa_listener";

        public const string RefreshTokenCookieName = "rt";
      //  public const string ImpersonationIdCookieName = "impersonation_session_id";
        public const string IdpSessionCookieName = "idp_session_id";

        public const string BlocksProviderName = "blocks-idp";
        public const string BlocksProviderType = "blocks";
        public const string OidcProtocol = "oidc";

        private const string DefaultProvider = "azure";
        private const string RabbitMqProvider = "rabbitmq";

        #region Identifier Service Constants
        //public const string IdentifierQueueName = "blocks_idp_identifier_listener";
        public const string DataCleanupQueue = "blocks_idp_data_cleanup_listener";
        public const string LanguageDataMigrationQueue = "blocks_idp_uilm_environment_data_migration_listener";
        public const string GenericMigrationQueue = "blocks_idp_generic_migration_listener";
        public const string MigrationCompletionTopic = "blocks_idp_migration_topic";
        public const string ProjectPeopleInvitationMailPurpose = "blocks_idp_project_invitation";
        public const string BlocsDomain = "seliseblocks.com";
        #endregion

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
                                             ConsumerSubscription.BindToQueue(IamQueue),
                                             ConsumerSubscription.BindToQueue(MfaQueueName),
                                             ConsumerSubscription.BindToQueue(DataCleanupQueue),
                                             ConsumerSubscription.BindToQueue(LanguageDataMigrationQueue),
                                             ConsumerSubscription.BindToQueue(GenericMigrationQueue),
                                             ConsumerSubscription.BindToQueue(IamOrgQueue)],
                }
            };
        }

        private static MessageConfiguration CreateAzureServiceBusConfiguration()
        {
            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [AuthenticationQueue, IamQueue, MfaQueueName, DataCleanupQueue, LanguageDataMigrationQueue, GenericMigrationQueue, IamOrgQueue],
                    Topics = [MigrationCompletionTopic]
                }
            };
        }

        public static CaptchaConfiguration MapToCaptchaConfiguration(Secret secret)
        {
            if (secret?.KeyValuePairs is not { } values)
            {
                return null;
            }

            return new CaptchaConfiguration
            {
                CaptchaKey = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaKey),
                CaptchaSecret = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaSecret),
                Provider = values.GetValueOrDefault(CaptchaSecretKeys.Provider),
                CaptchaGenerator = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaGenerator),
                IsEnable = bool.TryParse(values.GetValueOrDefault(CaptchaSecretKeys.IsEnable), out var isEnable) && isEnable
            };
        }
    }
}