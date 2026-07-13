using Iam.DomainService.Utilities;
using FluentAssertions;

namespace XUnitTest.Auth.Shared
{
    public class IdpConstantsTests
    {
        [Fact]
        public void QueueNames_AreCorrect()
        {
            IdpConstants.AuthenticationQueue.Should().Be("blocks_authentication_listener");
            IdpConstants.IamUserQueue.Should().Be("blocks_iam_listener_user");
            IdpConstants.IamResourceQueue.Should().Be("blocks_iam_listener_resource");
            IdpConstants.IamPermissionQueue.Should().Be("blocks_iam_listener_permission");
            IdpConstants.IamOrgQueue.Should().Be("blocks_iam_org_listener");
            IdpConstants.MailQueue.Should().Be("blocks_email_listener");
            IdpConstants.MfaQueueName.Should().Be("blocks_mfa_listener");
        }

        [Fact]
        public void CookieNames_AreCorrect()
        {
            IdpConstants.RefreshTokenCookieName.Should().Be("rt");
            IdpConstants.IdpSessionCookieName.Should().Be("idp_session_id");
        }

        [Theory]
        [InlineData("tenant-123", "idp_session_id_tenant-123")]
        [InlineData("", "idp_session_id")]
        [InlineData(null, "idp_session_id")]
        public void BuildIdpSessionCookieKey_AppendsTenantIdWhenProvided(string? tenantId, string expected)
        {
            IdpConstants.BuildIdpSessionCookieKey(tenantId).Should().Be(expected);
        }

        [Fact]
        public void ProviderNames_AreCorrect()
        {
            IdpConstants.BlocksProviderName.Should().Be("blocks-idp");
            IdpConstants.BlocksProviderType.Should().Be("blocks");
            IdpConstants.OidcProtocol.Should().Be("oidc");
        }

        [Fact]
        public void GetMessageConfiguration_ReturnsAzureConfig_ForNonAmqpConnectionString()
        {
            var config = IdpConstants.GetMessageConfiguration("Endpoint=sb://test.servicebus.windows.net/");

            config.Should().NotBeNull();
            config.AzureServiceBusConfiguration.Should().NotBeNull();
            config.AzureServiceBusConfiguration!.Queues.Should().Contain(IdpConstants.AuthenticationQueue);
        }

        [Fact]
        public void GetMessageConfiguration_ReturnsRabbitMqConfig_ForAmqpConnectionString()
        {
            var config = IdpConstants.GetMessageConfiguration("amqp://<username>:<password>@localhost:5672/");

            config.Should().NotBeNull();
            config.RabbitMqConfiguration.Should().NotBeNull();
            config.RabbitMqConfiguration!.ConsumerSubscriptions.Should().NotBeEmpty();
        }

        [Fact]
        public void GetMessageConfiguration_ReturnsRabbitMqConfig_ForAmqpsConnectionString()
        {
            var config = IdpConstants.GetMessageConfiguration("amqps://<username>:<password>@localhost:5671/");

            config.RabbitMqConfiguration.Should().NotBeNull();
        }

        [Fact]
        public void GetMessageConfiguration_FallsBackToAzure_ForUnparseableConnectionString()
        {
            var config = IdpConstants.GetMessageConfiguration("not-a-uri");

            config.AzureServiceBusConfiguration.Should().NotBeNull();
        }
    }
}