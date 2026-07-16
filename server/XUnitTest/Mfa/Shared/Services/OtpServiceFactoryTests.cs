using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Moq;
using StorageDriver;

namespace XUnitTest.Mfa.Shared.Services
{
    public class OtpServiceFactoryTests
    {
        [Fact]
        public void GetOTPService_ForTOTP_ReturnsTotpService()
        {
            var totpService = new TotpService(
                new Mock<IMfaManagementRepository>().Object,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TotpService>.Instance,
                new HttpContextAccessor(),
                new ConfigurationBuilder().Build(),
                new Mock<ICacheClient>().Object,
                new Mock<IValidator<VerifyOtpRequest>>().Object,
                new Mock<ITenants>().Object,
                new Mock<IStorageDriverService>().Object);

            var services = new ServiceCollection();
            services.AddSingleton(totpService);
            var sp = services.BuildServiceProvider();

            var factory = new OtpServiceFactory(sp);
            var result = factory.GetOTPService(UserMfaType.TOTP);

            result.Should().BeSameAs(totpService);
        }

        [Fact]
        public void GetOTPService_ForEmail_ReturnsEmailOtpService()
        {
            var emailService = new EmailOtpService(
                new Mock<ICacheClient>().Object,
                new Mock<IMfaConfigurationService>().Object,
                new Mock<IMessageClient>().Object);

            var services = new ServiceCollection();
            services.AddSingleton(emailService);
            var sp = services.BuildServiceProvider();

            var factory = new OtpServiceFactory(sp);
            var result = factory.GetOTPService(UserMfaType.Email);

            result.Should().BeSameAs(emailService);
        }

        [Fact]
        public void GetOTPService_ForUnknown_ThrowsArgumentException()
        {
            var sp = new ServiceCollection().BuildServiceProvider();
            var factory = new OtpServiceFactory(sp);

            Action act = () => factory.GetOTPService(UserMfaType.None);
            act.Should().Throw<ArgumentException>();
        }
    }
}
