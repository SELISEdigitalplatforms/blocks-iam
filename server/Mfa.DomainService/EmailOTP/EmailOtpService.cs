using Blocks.Genesis;
using Blocks.MailDriver;
using Iam.DomainService.Entities;
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
        private const int ResendCooldownInSecond = 60;
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
            var (email, sendPhoneNumberAsEmail, deliveryError) = ResolveDelivery(userInfo, sendPhoneNumberAsEmailDomain);
            if (deliveryError != null)
            {
                return deliveryError;
            }

            var context = MfaAuthenticationContext.Create(Guid.NewGuid().ToString(), userInfo.ItemId ?? string.Empty, UserMfaType.Email);
            context.SendPhoneNumberAsEmailDomain = sendPhoneNumberAsEmail ? sendPhoneNumberAsEmailDomain : null;

            await _cacheClient.AddStringValueAsync(context.MfaId ?? string.Empty, context.Sterilize(), DefaultLifeCycleInSecond);

            var result = await SendMfaCodeAsync(email ?? string.Empty, context.MfaCode ?? string.Empty, userInfo.Language ?? "en-US", sendPhoneNumberAsEmail);

            return new OtpGenerationResponse { MfaId = context.MfaId, IsSuccess = result };
        }

        public async Task<OtpGenerationResponse> ResendAsync(string mfaId, UserInfo userInfo, string? sendPhoneNumberAsEmailDomain = null)
        {
            if (string.IsNullOrWhiteSpace(mfaId) || !await _cacheClient.KeyExistsAsync(mfaId))
            {
                return new OtpGenerationResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "message", "invalid_two_factor_id" } } };
            }

            var raw = await _cacheClient.GetStringValueAsync(mfaId);
            if (!MfaAuthenticationContext.TryDeserialize(raw, out var context))
            {
                // Not a code-based challenge (e.g. a TOTP session) — nothing to resend.
                return new OtpGenerationResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "message", "resend_not_supported" } } };
            }

            var cooldown = TimeSpan.FromSeconds(ResendCooldownInSecond);
            var elapsed = DateTime.UtcNow - context.LastSentUtc;
            if (elapsed < cooldown)
            {
                var retryAfter = (int)Math.Ceiling((cooldown - elapsed).TotalSeconds);
                return new OtpGenerationResponse
                {
                    IsSuccess = false,
                    MfaId = mfaId,
                    Errors = new Dictionary<string, string>
                    {
                        { "message", "resend_too_soon" },
                        { "retry_after_seconds", retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                    }
                };
            }

            // The stored domain takes precedence so a resend re-routes to SMS even when the
            // caller omits it; an explicit caller value still wins when provided.
            var effectiveDomain = !string.IsNullOrWhiteSpace(sendPhoneNumberAsEmailDomain)
                ? sendPhoneNumberAsEmailDomain
                : context.SendPhoneNumberAsEmailDomain;

            var (email, sendPhoneNumberAsEmail, deliveryError) = ResolveDelivery(userInfo, effectiveDomain);
            if (deliveryError != null)
            {
                return deliveryError;
            }

            // Regenerate the code under the SAME mfa_id and reset the TTL, so the caller keeps
            // using the id it already has and the newly delivered code verifies against it.
            context.MfaCode = MfaAuthenticationContext.GenerateSecureRandomNumber();
            context.LastSentUtc = DateTime.UtcNow;
            context.SendPhoneNumberAsEmailDomain = sendPhoneNumberAsEmail ? effectiveDomain : null;

            await _cacheClient.AddStringValueAsync(mfaId, context.Sterilize(), DefaultLifeCycleInSecond);

            var result = await SendMfaCodeAsync(email ?? string.Empty, context.MfaCode ?? string.Empty, userInfo.Language ?? "en-US", sendPhoneNumberAsEmail);

            return new OtpGenerationResponse { MfaId = mfaId, IsSuccess = result };
        }

        private static (string? Email, bool SendPhoneNumberAsEmail, OtpGenerationResponse? Error) ResolveDelivery(UserInfo userInfo, string? sendPhoneNumberAsEmailDomain)
        {
            if (string.IsNullOrWhiteSpace(sendPhoneNumberAsEmailDomain))
            {
                return (userInfo.Email, false, null);
            }

            if (string.IsNullOrWhiteSpace(userInfo.PhoneNumber))
            {
                return (null, false, new OtpGenerationResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "phonenumber_not_exist", "PhoneNumber not exist in user for mfa" } } });
            }

            var email = $"{userInfo.PhoneNumber.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("+", "00", StringComparison.Ordinal)}@{sendPhoneNumberAsEmailDomain}";
            return (email, true, null);
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
