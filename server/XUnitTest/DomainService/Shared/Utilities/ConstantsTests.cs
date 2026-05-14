using System;
using CloudConfiguration.DomainService.Shared.Utilities;
using Xunit;
using FluentAssertions;
using System.Reflection;

namespace CloudConfiguration.DomainService.Shared.Utilities.Tests
{
    public class ConstantsTests
    {
        [Theory]
        [InlineData("amqp://localhost", "rabbitmq")]
        [InlineData("amqps://localhost", "rabbitmq")]
        [InlineData("https://servicebus.windows.net", "azure")]
        [InlineData("invalid", "azure")]
        [InlineData("", "azure")]
        public void GetMessageConfiguration_ReturnsExpectedProviderType(string connectionString, string expectedProvider)
        {
            var config = Constants.GetMessageConfiguration(connectionString);
            if (expectedProvider == "rabbitmq")
            {
                config.RabbitMqConfiguration.Should().NotBeNull();
                config.AzureServiceBusConfiguration.Should().BeNull();
            }
            else
            {
                config.AzureServiceBusConfiguration.Should().NotBeNull();
                config.RabbitMqConfiguration.Should().BeNull();
            }
        }

        [Fact]
        public void PublicConstants_ShouldHaveExpectedValues()
        {
            Constants.AuthenticationQueue.Should().Be("blocks_authentication_listener");
            Constants.DefaultMfaTemplateName.Should().Be("MfaViaEmail");
            Constants.DefaultMfaTemplateId.Should().Be("0b121378-3c3d-44f3-a855-9da08cbef48c");
            Constants.StorageQueue.Should().Be("blocks_storage_listener");
        }
    }
}
