using Blocks.Genesis;
using FluentValidation;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OtpNet;
using QRCoder;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using Blocks.Genesis.Entities.Iam;

namespace Mfa.DomainService.TOTP
{
    public class TotpService : IOtpService
    {
        private readonly IMfaManagementRepository _repository;
        private readonly ILogger<TotpService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;
        private readonly ICacheClient _cacheClient;
        private readonly IValidator<VerifyOtpRequest> _validator;
        private readonly ITenants _tenant;
        private readonly ITotpSecretResolver _secrets;

        private const long DefaultTotpLoginSession = 15 * 60;

        // How long a used time-step is remembered. Covers the accepted drift window (the current
        // step plus the previous one = 60s) with margin.
        private static readonly TimeSpan UsedTimeStepLifetime = TimeSpan.FromSeconds(120);

        // How long an unconfirmed setup keeps the same secret (page reloads, a slow scan).
        private static readonly TimeSpan PendingSetupLifetime = TimeSpan.FromMinutes(10);

        public TotpService(IMfaManagementRepository repository,
                           ILogger<TotpService> logger,
                           IHttpContextAccessor httpContextAccessor,
                           IConfiguration configuration,
                           ICacheClient cacheClient,
                           IValidator<VerifyOtpRequest> validator,
                           ITenants tenant,
                           ITotpSecretResolver secrets)
        {
            _repository = repository;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
            _cacheClient = cacheClient;
            _validator = validator;
            _tenant = tenant;
            _secrets = secrets;
        }

        public async Task<OtpGenerationResponse> GenerateAsync(UserInfo userInfo, string? sendPhoneNumberAsEmailDomain = null)
        {
            var mfaId = Guid.NewGuid().ToString();
            await _cacheClient.AddStringValueAsync(mfaId, userInfo.ItemId ?? string.Empty, DefaultTotpLoginSession);
            return new OtpGenerationResponse { IsSuccess = true, MfaId = mfaId };
        }

        public async Task<SetUpUserTotpResponse> GenerateTotpImageByUserAsync(string userId)
        {
            var userInfo = await _repository.GetItemAsync<UserInfo>(u => u.ItemId == userId, "Users");

            if (userInfo is null) { return new SetUpUserTotpResponse { Errors = new Dictionary<string, string> { { "user_not_exist", $"No user exist with useId: {userId}" } } }; }

            // An active authenticator is never shown again: whoever holds a session must not be
            // able to copy the secret. Changing the device = disable (with re-check), then set up.
            if (userInfo.MfaEnabled && userInfo.UserMfaType == UserMfaType.TOTP)
            {
                return new SetUpUserTotpResponse { Errors = new Dictionary<string, string> { { "totp_already_enabled", "TOTP is already enabled for this user" } } };
            }

            string secret;
            try
            {
                secret = await GetOrCreatePendingSecretAsync(userId);
            }
            catch (TotpSecretUnavailableException)
            {
                return new SetUpUserTotpResponse { Errors = new Dictionary<string, string> { { "totp_unavailable", "TOTP setup is not possible right now. Try again later." } } };
            }

            var qrImage = GenerateQrCodeImageData(GetIssuer(), userInfo.Email ?? string.Empty, secret);

            // The QR holds the secret, so it is sent once in this response and never stored as a file.
            return CreateOtpResponse(true, $"data:image/png;base64,{Convert.ToBase64String(qrImage)}", secret);
        }

        // A setup that is not confirmed yet keeps its secret for a short time, so a page reload
        // shows the same QR the user may already have scanned. Anything older, or left from before
        // this change, is replaced by a new secret. The value lives only in Blocks Secrets.
        private async Task<string> GetOrCreatePendingSecretAsync(string userId)
        {
            var existing = await GetExistingOtpInfoAsync(userId);
            if (existing is not null
                && !string.IsNullOrWhiteSpace(existing.SecretReference)
                && string.IsNullOrEmpty(existing.ImageUri)
                && existing.CreatedDate > DateTime.UtcNow - PendingSetupLifetime)
            {
                var pending = await ReadSecretAsync(userId, existing.SecretReference);
                if (pending is not null)
                {
                    return pending;
                }
            }

            await TotpSecretRows.DeleteAsync(_repository, _secrets, userId);

            var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
            var reference = await _secrets.StoreAsync(userId, BlocksContext.GetContext()?.OrganizationId, secret);
            try
            {
                await SaveOtpInfoAsync(userId, reference);
            }
            catch
            {
                // No row points at it, so the stored secret must not stay behind.
                await _secrets.RetireAsync(userId, reference);
                throw;
            }

            return secret;
        }

        private string GetIssuer()
        {
            var tenant = _tenant.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? string.Empty);
            return tenant?.Applications != null && tenant.Applications.Count > 0
                ? tenant.Applications[0].Domain?.Replace("https://", string.Empty, StringComparison.Ordinal) ?? string.Empty
                : string.Empty;
        }

        private async Task<UserTotpDetail?> GetExistingOtpInfoAsync(string userId)
        {
            return (await _repository.GetItemAsync<UserTotpDetail>(t => t.CreatedBy == userId));
        }

        private async Task SaveOtpInfoAsync(string userId, string secretReference)
        {
            var otpInfo = new UserTotpDetail
            {
                CreatedBy = userId,
                LastUpdatedBy = userId,
                Secret = string.Empty,
                SecretReference = secretReference,
                ImageUri = string.Empty,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                TowFactorId = Guid.NewGuid().ToString(),
                ItemId = Guid.NewGuid().ToString()
            };

            await _repository.SaveAsync(otpInfo);
        }

        private static byte[] GenerateQrCodeImageData(string issuer, string email, string secret)
        {
            string tOTPUri = $"otpauth://totp/{issuer}:{email}?secret={secret}";
            using var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(tOTPUri, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            return qrCode.GetGraphic(20);
        }

        private static SetUpUserTotpResponse CreateOtpResponse(bool isSuccess, string imageUri = "", string code = "")
        {
            return new SetUpUserTotpResponse
            {
                IsSuccess = isSuccess,
                QrImageUrl = imageUri,
                QrCode = code
            };
        }

        public Task<OtpGenerationResponse> ResendAsync(string mfaId, UserInfo userInfo, string? sendPhoneNumberAsEmailDomain = null)
        {
            // TOTP codes are produced by the user's authenticator app; there is nothing for the
            // server to re-deliver, so resend is not applicable to this method.
            return Task.FromResult(new OtpGenerationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string> { { "message", "resend_not_supported" } }
            });
        }

        public async Task<OtpVerificationResponse> VerifyAsync(VerifyOtpRequest request)
        {
            var validator = await _validator.ValidateAsync(request);

            if (!validator.IsValid)
            {
                return new OtpVerificationResponse { Errors = validator.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
            }

            if (!await _cacheClient.KeyExistsAsync(request.MfaId ?? string.Empty))
            {
                return new OtpVerificationResponse { Errors = new Dictionary<string, string> { { "login_session_expired", "User login session expire" } } };
            }

            var userId = await _cacheClient.GetStringValueAsync(request.MfaId ?? string.Empty);
            var tOTPInfo = await _repository.GetItemAsync<UserTotpDetail>(t => t.CreatedBy == userId);

            if (tOTPInfo is null || string.IsNullOrWhiteSpace(tOTPInfo.SecretReference))
            {
                return TotpNotSetUp();
            }

            var secret = await ReadSecretAsync(userId ?? string.Empty, tOTPInfo.SecretReference);
            if (secret is null)
            {
                return TotpUnavailable();
            }

            var tOTP = new Totp(Base32Encoding.ToBytes(secret));
            bool isValid = tOTP.VerifyTotp(request.VerificationCode ?? string.Empty, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

            // Single use. Deleting the login session is the atomic gate: of any number of
            // parallel submits with the same mfa_id, only the one that really deleted the key
            // may continue.
            // A time-step is single use per user: the same code (or the same step) cannot be
            // replayed under a new mfa_id. SET NX is atomic, so parallel submits race safely.
            if (isValid
                && (!await _cacheClient.RemoveKeyAsync(request.MfaId ?? string.Empty)
                    || !await _cacheClient.CacheDatabase().StringSetAsync(
                        $"totp:used:{tOTPInfo.CreatedBy}:{timeStepMatched}", "1", UsedTimeStepLifetime, When.NotExists)))
            {
                isValid = false;
            }

            return new OtpVerificationResponse { IsSuccess = true, IsValid = isValid, UserId = tOTPInfo.CreatedBy };
        }

        public async Task<OtpVerificationResponse> VerifyForUserAsync(string userId, string verificationCode)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(verificationCode))
            {
                return new OtpVerificationResponse { Errors = new Dictionary<string, string> { { "invalid_request", "userId and verificationCode are required" } } };
            }

            var existing = await GetExistingOtpInfoAsync(userId);
            if (existing == null || string.IsNullOrWhiteSpace(existing.SecretReference))
            {
                return TotpNotSetUp();
            }

            var secret = await ReadSecretAsync(userId, existing.SecretReference);
            if (secret is null)
            {
                return TotpUnavailable();
            }

            var totp = new Totp(Base32Encoding.ToBytes(secret));
            var isValid = totp.VerifyTotp(verificationCode, out _, VerificationWindow.RfcSpecifiedNetworkDelay);

            return new OtpVerificationResponse { IsSuccess = true, IsValid = isValid, UserId = userId };
        }

        // Only the secret store holds the value: a row without a reference (an old plaintext secret)
        // counts as not set up, and a failed read fails the check (owner 2026-11-01).
        private async Task<string?> ReadSecretAsync(string userId, string reference)
        {
            try
            {
                return await _secrets.GetAsync(userId, reference);
            }
            catch (TotpSecretUnavailableException)
            {
                return null;
            }
        }

        private static OtpVerificationResponse TotpNotSetUp() =>
            new() { Errors = new Dictionary<string, string> { { "totp_not_setup", "TOTP has not been set up for this user" } } };

        private static OtpVerificationResponse TotpUnavailable() =>
            new() { Errors = new Dictionary<string, string> { { "totp_unavailable", "TOTP cannot be checked right now. Try again or use another sign-in method." } } };
    }
}
