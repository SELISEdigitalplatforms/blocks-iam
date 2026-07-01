using FluentAssertions;
using Mfa.DomainService.Utilities;

namespace XUnitTest.Mfa.Shared.Utilities
{
    public class MfaConstantsTests
    {
        [Fact]
        public void GetMessageConfiguration_ContainsMfaQueueName()
        {
            var config = MfaConstants.GetMessageConfiguration();

            config.Should().NotBeNull();
            config.AzureServiceBusConfiguration.Should().NotBeNull();
            config.AzureServiceBusConfiguration.Queues.Should().Contain(MfaConstants.MfaQueueName);
        }

        [Fact]
        public void GetMessageConfiguration_Topics_IsEmpty()
        {
            var config = MfaConstants.GetMessageConfiguration();

            config.AzureServiceBusConfiguration.Topics.Should().BeEmpty();
        }

        [Theory]
        [InlineData("ApiServiceName")]
        [InlineData("WorkerServiceName")]
        [InlineData("DefaultMfaTemplateName")]
        [InlineData("DefaultMfaTemplateId")]
        [InlineData("MfaQueueName")]
        [InlineData("AuthenticationQueue")]
        public void PublicConstants_AreNotNullOrEmpty(string fieldName)
        {
            var field = typeof(MfaConstants).GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            field.Should().NotBeNull();
            var value = field!.GetRawConstantValue() as string;
            value.Should().NotBeNullOrEmpty();
        }
    }
}
