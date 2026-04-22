using Api.Controllers;
using Captcha.DomainService.Captcha;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;

namespace XUnitTest.Controllers
{
    public class CaptchaControllerTests
    {
        private readonly Mock<ICaptchaService> _captchaService = new();
        private readonly Mock<IValidator<CreateCaptchaRequest>> _createCaptchaValidator = new();
        private readonly CaptchaController _captchaController;

        public CaptchaControllerTests()
        {
            _captchaController = new CaptchaController(_captchaService.Object);
        }

        [Fact]
        public void Create_Should_Return_Response_From_Service()
        {
            var request = new CreateCaptchaRequest { ConfigurationName  = "default"};
            var validationResult = new ValidationResult(); // valid result

            _createCaptchaValidator
                .Setup(v => v.Validate(It.IsAny<CreateCaptchaRequest>()))
                .Returns(validationResult);

            var response = new CreateCaptchaRequestResponse(validationResult)
            {
                IsSuccess = true,
                Captcha = "captcha",
                Id = "id"
            };

            _captchaService
                .Setup(x => x.CreateCaptcha(request))
                .Returns(response);

            var result = _captchaController.Create(request);

            result.Should().BeEquivalentTo(response);
        }

        [Fact]
        public async Task Submit_Should_Return_Response_From_Service()
        {
            var request = new SubmitCaptchaRequest();
            var validationResult = new ValidationResult(); // valid result

            _createCaptchaValidator
                .Setup(v => v.Validate(It.IsAny<CreateCaptchaRequest>()))
                .Returns(validationResult);

            var response = new SubmitCaptchaRequestResponse(validationResult)
            {
                IsSuccess = true,
                VerificationCode = "code"
            };

            _captchaService
                .Setup(x => x.SubmitCaptchaAsync(request))
                .ReturnsAsync(response);

            var result = await _captchaController.Submit(request);

            result.Should().BeEquivalentTo(response);
        }

        [Fact]
        public async Task Verify_Should_Return_Response_From_Service()
        {
            var request = new VerifyCaptchaRequest();

            var response = new VerifyCaptchaRequestResponse
            {
                IsSuccess = true
            };

            _captchaService
                .Setup(x => x.VerifyCaptchaAsync(request))
                .ReturnsAsync(response);

            var result = await _captchaController.Verify(request);

            result.Should().BeEquivalentTo(response);
        }
    }
}
