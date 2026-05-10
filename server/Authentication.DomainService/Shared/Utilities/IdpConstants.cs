using Blocks.Genesis;
using Microsoft.Extensions.Configuration;

namespace Authentication.DomainService.Utilities
{
    public static class IdpConstants
    {
        public const string TenantTokenPublicCertificateCachePrefix = "tetocertpublic::";
        public const string AuthenticationQueue = "blocks_idp_authentication_listener";
        public const string IamQueue = "blocks_idp_iam_listener";
        public const string MailQueue = "blocks_idp_mail_listener";
        public const string MfaQueueName = "blocks_idp_mfa_listener";

        public const string AccessTokenCookieName = "access_token";
        public const string RefreshTokenCookieName = "refresh_token";
        public const string IdTokenCookieName = "id_token";

        public const string BlocksProviderName = "blocks-idp";
        public const string BlocksProviderType = "blocks";
        public const string OidcProtocol = "oidc";

        private const string DefaultProvider = "azure";
        private const string RabbitMqProvider = "rabbitmq";

        #region Identifier Service Constants
        public const string IdentifierQueueName = "blocks_idp_identifier_listener";
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
                                             ConsumerSubscription.BindToQueue(IdentifierQueueName),
                                             ConsumerSubscription.BindToQueue(DataCleanupQueue),
                                             ConsumerSubscription.BindToQueue(LanguageDataMigrationQueue),
                                             ConsumerSubscription.BindToQueue(GenericMigrationQueue)],
                }
            };
        }

        private static MessageConfiguration CreateAzureServiceBusConfiguration()
        {
            return new MessageConfiguration
            {
                AzureServiceBusConfiguration = new AzureServiceBusConfiguration
                {
                    Queues = [AuthenticationQueue, IamQueue, MfaQueueName, IdentifierQueueName, DataCleanupQueue, LanguageDataMigrationQueue, GenericMigrationQueue],
                    Topics = [MigrationCompletionTopic]
                }
            };
        }
    }
}