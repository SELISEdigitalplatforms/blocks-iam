using CloudConfiguration.DomainService.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FluentAssertions;
using CloudConfiguration.DomainService.Shared.Services;
using FluentValidation;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.IAM.RequestModel;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Storage.RequestModel;
using CloudConfiguration.DomainService.Mail.RequestModel;
using Moq;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace CloudConfiguration.DomainService.Shared.Utilities.Tests
{
    public class ApplicationServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddCloudConfigurationServices_RegistersExpectedServices()
        {
            // Arrange
            var services = new ServiceCollection();

            // Register mocks for required dependencies
            var dbContextProviderMock = new Mock<IDbContextProvider>();
            var messageClientMock = new Mock<IMessageClient>();
            var loggerMock = new Mock<ILogger<ConfigurationService>>();
            var tenantsMock = new Mock<ITenants>();
            services.AddSingleton<IDbContextProvider>(dbContextProviderMock.Object);
            services.AddSingleton<IMessageClient>(messageClientMock.Object);
            services.AddSingleton(typeof(ILogger<ConfigurationService>), loggerMock.Object);
            services.AddSingleton<ITenants>(tenantsMock.Object);

            // Act
            services.AddCloudConfigurationServices();
            var provider = services.BuildServiceProvider();

            // Assert
            provider.GetService<IConfigurationService>().Should().NotBeNull();
            provider.GetService<IConfigurationRepository>().Should().NotBeNull();
            provider.GetService<IValidator<SaveCaptchaConfigurationRequest>>().Should().NotBeNull();
            provider.GetService<IValidator<SaveIamConfigurationRequest>>().Should().NotBeNull();
            provider.GetService<IValidator<SaveNotificatonConfigurationRequest>>().Should().NotBeNull();
            provider.GetService<IValidator<SaveStorageConfigurationRequest>>().Should().NotBeNull();
            provider.GetService<IValidator<MailConfiguration>>().Should().NotBeNull();
        }
    }
}
