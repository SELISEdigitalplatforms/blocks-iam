using Blocks.Genesis;
using DomainService.Storage;
using FluentValidation;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OtpNet;
using QRCoder;
using StorageDriver;
using System.Net;
using System.Text;
using System.Text.Json;

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
        private readonly IStorageDriverService _storageDriverService;
        private HttpClient _httpClient;

        private const long DefaultTotpLoginSession = 15 * 60;

        public TotpService(IMfaManagementRepository repository,
                           ILogger<TotpService> logger,
                           IHttpContextAccessor httpContextAccessor,
                           IConfiguration configuration,
                           ICacheClient cacheClient,
                           IValidator<VerifyOtpRequest> validator,
                           ITenants tenant,
                           IStorageDriverService storageDriverService)
        {
            _repository = repository;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
            _cacheClient = cacheClient;
            _validator = validator;
            _tenant = tenant;
            _storageDriverService = storageDriverService;
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

            //var secret = ExtractUserSecret(Guid.NewGuid().ToString());
            var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
            var existingOtpInfo = await GetExistingOtpInfoAsync(userId);

            if (existingOtpInfo is not null && !string.IsNullOrWhiteSpace(existingOtpInfo.ImageUri))
            {
                return new SetUpUserTotpResponse { IsSuccess = true, QrImageUrl = existingOtpInfo.ImageUri, QrCode = existingOtpInfo.Secret };
            }

            var fileId = GenerateGuid();
            var twoFactorId = GenerateGuid();
            var preSignedUrl = await GetPreSignedUrlAsync(fileId);
            var tenant = _tenant.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? string.Empty);
            var applicationDomain = tenant?.Applications != null && tenant.Applications.Count > 0
                ? tenant.Applications[0].Domain?.Replace("https://", string.Empty, StringComparison.Ordinal) ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(preSignedUrl)) { return new SetUpUserTotpResponse { Errors = new Dictionary<string, string> { { "configuration_not_exit", "please_check_default_storage_configuration" } } }; }

            var qrCodeData = GenerateQrCodeImageData(applicationDomain, userInfo.Email ?? string.Empty, secret);
            var response = await UploadQrCodeAsync(preSignedUrl, qrCodeData);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                _logger.LogError("QR code upload failed. statusCode: {0}", response.StatusCode);
                return CreateOtpResponse(false);
            }

            var imageUri = await GetFileUriAsync(fileId);

            if (!string.IsNullOrWhiteSpace(imageUri))
            {
                await SaveOtpInfoAsync(userInfo.ItemId ?? string.Empty, secret, imageUri, fileId, twoFactorId);
                return CreateOtpResponse(true, imageUri, secret);
            }

            return CreateOtpResponse(false);
        }

        private static string ExtractUserSecret(string userId) => string.Concat(userId.Where(char.IsLetter));
        private static string GenerateGuid() => Guid.NewGuid().ToString();

        private async Task<UserTotpDetail?> GetExistingOtpInfoAsync(string userId)
        {
            return (await _repository.GetItemAsync<UserTotpDetail>(t => t.CreatedBy == userId));
        }

        private async Task<string> GetPreSignedUrlAsync(string fileId)
        {
            var requestBody = new GetPreSignedUrlForUploadRequest
            {
                ItemId = fileId,
                MetaData = "{\"Title\":{\"Type\":\"String\",\"Value\":\"QrImage.png\"},\"OriginalName\":{\"Type\":\"String\",\"Value\":\"image\"}}",
                Name = "QrImage.png",
                Tags = "[\"File\"]",
                ParentDirectoryId = string.Empty,
                AccessModifier = "Public"
            };


            var presignedUrlResponse = await _storageDriverService.GetPerSignedUrlForUploadAsync(requestBody);
            return presignedUrlResponse.UploadUrl;
        }

        private async Task<string> GetFileUriAsync(string fileId)
        {

            var fileUriResponse = await _storageDriverService.GetUrlForDownloadFileAsync(new GetFileRequest { FileId = fileId });
            return fileUriResponse.Url;
        }

        private async Task<HttpResponseMessage> UploadQrCodeAsync(string preSignedUrl, byte[] qrCodeData)
        {
            _httpClient = new HttpClient();

            using var request = new HttpRequestMessage(HttpMethod.Put, preSignedUrl)
            {
                Content = new ByteArrayContent(qrCodeData)
            };

            request.Headers.Add("x-ms-blob-type", "BlockBlob");

            return await _httpClient.SendAsync(request);
        }

        private async Task SaveOtpInfoAsync(string userId, string secret, string imageUri, string fileId, string twoFactorId)
        {
            var otpInfo = new UserTotpDetail
            {
                CreatedBy = userId,
                LastUpdatedBy = userId,
                Secret = secret,
                ImageUri = imageUri,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                TowFactorId = twoFactorId,
                ItemId = fileId
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

            if (tOTPInfo is null || string.IsNullOrWhiteSpace(tOTPInfo.Secret))
            {
                return new OtpVerificationResponse { Errors = new Dictionary<string, string> { { "totp_not_setup", "TOTP has not been set up for this user" } } };
            }

            var key = Base32Encoding.ToBytes(tOTPInfo.Secret);
            var tOTP = new Totp(key);
            bool isValid = tOTP.VerifyTotp(request.VerificationCode ?? string.Empty, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

            return new OtpVerificationResponse { IsSuccess = true, IsValid = isValid, UserId = tOTPInfo.CreatedBy };
        }

        public async Task<OtpVerificationResponse> VerifyForUserAsync(string userId, string verificationCode)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(verificationCode))
            {
                return new OtpVerificationResponse { Errors = new Dictionary<string, string> { { "invalid_request", "userId and verificationCode are required" } } };
            }

            var existing = await GetExistingOtpInfoAsync(userId);
            if (existing == null || string.IsNullOrWhiteSpace(existing.Secret))
            {
                return new OtpVerificationResponse { Errors = new Dictionary<string, string> { { "totp_not_setup", "TOTP has not been set up for this user" } } };
            }

            var key = Base32Encoding.ToBytes(existing.Secret);
            var totp = new Totp(key);
            var isValid = totp.VerifyTotp(verificationCode, out _, VerificationWindow.RfcSpecifiedNetworkDelay);

            return new OtpVerificationResponse { IsSuccess = true, IsValid = isValid, UserId = userId };
        }
    }
}
