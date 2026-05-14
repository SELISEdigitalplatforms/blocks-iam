using Xunit;
using Moq;
using FluentAssertions;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Captcha.RequestModel;
using CloudConfiguration.DomainService.Captcha.ResponseModel;
using CloudConfiguration.DomainService.Captcha.Entities;
using CloudConfiguration.DomainService.IAM.RequestModel;
using CloudConfiguration.DomainService.IAM.Entities;
using CloudConfiguration.DomainService.IAM.ResponseModel;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Storage.RequestModel;
using CloudConfiguration.DomainService.Mail.RequestModel;
using FluentValidation;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Collections.Generic;
using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Shared.Services.Tests
{
    public class ConfigurationServiceTests
    {
        private readonly Mock<IConfigurationRepository> _repo = new();
        private readonly Mock<IValidator<SaveCaptchaConfigurationRequest>> _captchaValidator = new();
        private readonly Mock<IValidator<SaveIamConfigurationRequest>> _iamValidator = new();
        private readonly Mock<IValidator<SaveNotificatonConfigurationRequest>> _notificationValidator = new();
        private readonly Mock<IValidator<SaveStorageConfigurationRequest>> _storageValidator = new();
        private readonly Mock<IValidator<MailConfiguration>> _mailValidator = new();
        private readonly Mock<IMessageClient> _messageClient = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ILogger<ConfigurationService>> _logger = new();

        private ConfigurationService CreateService() => new(
            _repo.Object,
            _captchaValidator.Object,
            _iamValidator.Object,
            _notificationValidator.Object,
            _storageValidator.Object,
            _mailValidator.Object,
            _messageClient.Object,
            _logger.Object,
            _tenants.Object
        );

        [Fact]
        public async Task GetCaptchaConfigurationAsync_Returns_Configuration_When_Exists()
        {
            var captchaConfig = new CaptchaConfiguration { ItemId = "id", Provider = "provider" };
            _repo.Setup(r => r.GetCaptchaConfigurationByProviderAsync("provider")).ReturnsAsync(captchaConfig);
            var service = CreateService();

            var result = await service.GetCaptchaConfigurationAsync("provider");

            result.Should().NotBeNull();
            result.Configuration.Should().Be(captchaConfig);
            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetCaptchaConfigurationAsync_Returns_Error_When_Not_Exists()
        {
            _repo.Setup(r => r.GetCaptchaConfigurationByProviderAsync("provider")).ReturnsAsync((CaptchaConfiguration)null);
            var service = CreateService();

            var result = await service.GetCaptchaConfigurationAsync("provider");

            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.Errors.Should().ContainKey("no_configuration_exist");
        }

        [Fact]
        public async Task SaveCaptchaConfigurationAsync_Returns_Error_On_Validation_Failure()
        {
            var request = new SaveCaptchaConfigurationRequest { Provider = "provider" };
            var validationResult = new FluentValidation.Results.ValidationResult(new[] {
                new FluentValidation.Results.ValidationFailure("Provider", "Required")
            });
            _captchaValidator.Setup(v => v.ValidateAsync(request, default)).ReturnsAsync(validationResult);
            var service = CreateService();

            var result = await service.SaveCaptchaConfigurationAsync(request);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Provider");
        }

        [Fact]
        public async Task SaveCaptchaConfigurationAsync_Successful_Save()
        {
            var request = new SaveCaptchaConfigurationRequest { Provider = "provider" };
            _captchaValidator.Setup(v => v.ValidateAsync(request, default)).ReturnsAsync(new FluentValidation.Results.ValidationResult());
            _repo.Setup(r => r.GetCaptchaConfigurationByProviderAsync(request.Provider)).ReturnsAsync((CaptchaConfiguration)null);
            _repo.Setup(r => r.SaveCaptchaConfigurationAsync(It.IsAny<CaptchaConfiguration>())).Returns(Task.CompletedTask);
            var service = CreateService();

            var result = await service.SaveCaptchaConfigurationAsync(request);

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task SaveIamConfigurationAsync_Returns_Error_On_Validation_Failure()
        {
            var request = new SaveIamConfigurationRequest();
            var validationResult = new FluentValidation.Results.ValidationResult(new[] {
                new FluentValidation.Results.ValidationFailure("AccountActivationUrl", "Required")
            });
            _iamValidator.Setup(v => v.ValidateAsync(request, default)).ReturnsAsync(validationResult);
            var service = CreateService();

            var result = await service.SaveIamConfigurationAsync(request);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("AccountActivationUrl");
        }

        [Fact]
        public async Task SaveIamConfigurationAsync_Successful_Save()
        {
            var request = new SaveIamConfigurationRequest();
            _iamValidator.Setup(v => v.ValidateAsync(request, default)).ReturnsAsync(new FluentValidation.Results.ValidationResult());
            _repo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync((IamConfiguration)null);
            _repo.Setup(r => r.SaveIamConfigurationAsync(It.IsAny<IamConfiguration>())).Returns(Task.CompletedTask);
            var service = CreateService();

            var result = await service.SaveIamConfigurationAsync(request);

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetIamConfigurationAsync_Returns_Data()
        {
            var iamConfig = new IamConfiguration();
            _repo.Setup(r => r.GetIamConfigurationAsync()).ReturnsAsync(iamConfig);
            var service = CreateService();

            var result = await service.GetIamConfigurationAsync();

            result.Data.Should().Be(iamConfig);
        }
    }
}
