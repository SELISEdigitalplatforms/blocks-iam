using Blocks.Genesis;
using Blocks.MailDriver;
using Iam.DomainService.Utilities;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;

namespace Mfa.DomainService.OTP.Services
{
    public class EmailOtpService : IOtpService
    {
        private readonly ICacheClient _cacheClient;
        private readonly IMfaConfigurationService _configurationService;
        private readonly IMessageClient _messageClient;

        private const int DefaultLifeCycleInSecond = 300;
        private const string DefaultMfaTemplate = "MfaViaEmail";

        public EmailOtpService(ICacheClient cacheClient,
                               IMfaConfigurationService configurationService,
                               IMessageClient messageClient)
        {
            _cacheClient = cacheClient;
            _configurationService = configurationService;
            _messageClient = messageClient;
        }

        public async Task<OtpGenerationResponse> GenerateAsync(UserInfo userInfo, string? sendPhoneNumberAsEmailDomain = null)
        {
            var context = MfaAuthenticationContext.Create(Guid.NewGuid().ToString(), userInfo.ItemId ?? string.Empty);
            var code = context.MfaCode;

            await _cacheClient.AddStringValueAsync(context.MfaId ?? string.Empty, context.Sterilize(), DefaultLifeCycleInSecond);
            var email = userInfo.Email;
            var sendPhoneNumberAsEmail = false;

            if (!string.IsNullOrWhiteSpace(sendPhoneNumberAsEmailDomain))
            {
                if (string.IsNullOrWhiteSpace(userInfo.PhoneNumber))
                    return new OtpGenerationResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "phonenumber_not_exist", "PhoneNumber not exist in user for mfa" } } };

                email = $"{userInfo.PhoneNumber.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("+", "00", StringComparison.Ordinal)}@{sendPhoneNumberAsEmailDomain}";
                sendPhoneNumberAsEmail = true;
            }

            var result = await SendMfaCodeAsync(email ?? string.Empty, code ?? string.Empty, userInfo.Language ?? "en-US", sendPhoneNumberAsEmail);

            return new OtpGenerationResponse { MfaId = context.MfaId, IsSuccess = result };
        }

        private async Task<bool> SendMfaCodeAsync(string email, string code, string language, bool sendPhoneNumberAsEmail = false)
        {
            var configuration = await _configurationService.GetAsync();

            var sendMailCommand = new SendMail
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = new Dictionary<string, string>
                                {
                                   { "TwoFactorCode", code }
                                },

                Purpose = !string.IsNullOrWhiteSpace(configuration?.MfaTemplate?.TemplateName) ? configuration.MfaTemplate.TemplateName : DefaultMfaTemplate,
                Language = language,
                To = [email],
                SendPhoneNumberAsEmail = sendPhoneNumberAsEmail
            };

            await _messageClient.SendToConsumerAsync(new ConsumerMessage<SendMail>
            {
                ConsumerName = IdpConstants.MailQueue,
                Payload = sendMailCommand
            });

            return true;
        }

        public async Task<OtpVerificationResponse> VerifyAsync(VerifyOtpRequest request)
        {
            var isKeyExist = await _cacheClient.KeyExistsAsync(request.MfaId ?? string.Empty);

            if (!isKeyExist)
            {
                return new OtpVerificationResponse { Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_id" } } };
            }

            var keyValue = await _cacheClient.GetStringValueAsync(request.MfaId ?? string.Empty);
            var mfaContext = MfaAuthenticationContext.Deserialize(keyValue);

            if (mfaContext.MfaCode == request.VerificationCode)
            {
                await _cacheClient.RemoveKeyAsync(request.MfaId ?? string.Empty);
                return new OtpVerificationResponse { IsSuccess = true, IsValid = true, UserId = mfaContext.UserId };
            }

            return new OtpVerificationResponse { Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_code" } } };
        }
    }
}
