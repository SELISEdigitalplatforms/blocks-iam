using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Verifies hCaptcha tokens against the hCaptcha siteverify endpoint.
/// </summary>
public sealed class HCaptchaVerificationService : ICaptchaVerificationService
{
    /// <inheritdoc />
    public string Provider => "hcaptcha";

    private const string LogContentType = "application/x-www-form-urlencoded";

    private readonly ICaptchaConfigurationService _captchaConfigurationService;
    private readonly ICaptchaSecretResolver _secretResolver;
    private readonly ILogger<HCaptchaVerificationService> _logger;
    private readonly IHttpClientService _httpClientService;
    private readonly string _verificationUrl;

    public HCaptchaVerificationService(
        ICaptchaConfigurationService captchaConfigurationService,
        ICaptchaSecretResolver secretResolver,
        IOptions<CaptchaOptions> options,
        ILogger<HCaptchaVerificationService> logger,
        IHttpClientService httpClientService)
    {
        _captchaConfigurationService = captchaConfigurationService;
        _secretResolver = secretResolver;
        _logger = logger;
        _httpClientService = httpClientService;
        _verificationUrl = options.Value.HcaptchaVerificationUrl;
    }

    /// <inheritdoc />
    public async Task<VerificationResult> VerifyAsync(string verificationCode)
    {
        _logger.LogDebug("hCaptcha verification requested");

        var hcaptchaResponse = await VerifyCaptchaAsync(verificationCode);

        if (hcaptchaResponse is { Success: true })
        {
            return new VerificationResult
            {
                Verified = true,
                HostName = hcaptchaResponse.HostName ?? string.Empty
            };
        }

        return new VerificationResult
        {
            Verified = false,
            Errors = new Dictionary<string, string>
            {
                { "VerificationCode", "Verification failed" }
            }
        };
    }

    private async Task<RecaptchaResponse> VerifyCaptchaAsync(string token)
    {
        try
        {
            var dbConfig = await _captchaConfigurationService.GetCaptchaConfigurationAsync();
            if (dbConfig is null)
            {
                _logger.LogError("hCaptcha verification: no captcha configuration found in store");
                return new RecaptchaResponse { Success = false };
            }

            // Either the inline legacy value or, for a blocks-os configuration, the plaintext
            // behind SecretId. Unresolvable means fail closed, never fall back.
            var secretKey = await dbConfig.ResolveSecretAsync(_secretResolver).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(secretKey))
            {
                _logger.LogError("hCaptcha verification: no usable captcha secret for this tenant");
                return new RecaptchaResponse { Success = false };
            }

            var contentPayloads = new List<KeyValuePair<string, string>>
            {
                new("secret", secretKey),
                new("response", token)
            };

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(_verificationUrl),
                Content = new FormUrlEncodedContent(contentPayloads)
            };

            var response = await _httpClientService.SendAsync(request, LogContentType);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "hCaptcha siteverify call failed. Status: {StatusCode}",
                    response.StatusCode);
                return new RecaptchaResponse { Success = false };
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            var verifyResponse = await JsonSerializer.DeserializeAsync<RecaptchaResponse>(
                contentStream,
                JsonOptions.Default);

            return verifyResponse ?? new RecaptchaResponse { Success = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "hCaptcha verification failed");
            return new RecaptchaResponse { Success = false };
        }
    }
}
