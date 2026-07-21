using System.Text.Json;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Verifies Google reCAPTCHA tokens against Google's siteverify endpoint.
/// </summary>
public sealed class ReCaptchaVerificationService : ICaptchaVerificationService
{
    /// <inheritdoc />
    public string Provider => "recaptcha";

    private const string LogContentType = "application/x-www-form-urlencoded";

    private readonly IHttpClientService _httpClientService;
    private readonly ILogger<ReCaptchaVerificationService> _logger;
    private readonly IRecaptchaConfigFactory _recaptchaConfigFactory;
    private readonly string _recaptchaVerificationUrl;

    public ReCaptchaVerificationService(
        IHttpClientService httpClientService,
        IOptions<CaptchaOptions> options,
        ILogger<ReCaptchaVerificationService> logger,
        IRecaptchaConfigFactory recaptchaConfigFactory)
    {
        _httpClientService = httpClientService;
        _logger = logger;
        _recaptchaConfigFactory = recaptchaConfigFactory;
        _recaptchaVerificationUrl = options.Value.RecaptchaVerificationUrl;
    }

    /// <inheritdoc />
    public async Task<VerificationResult> VerifyAsync(string verificationCode)
    {
        _logger.LogDebug("ReCaptcha verification requested");

        var recaptchaResponse = await VerifyCaptchaAsync(verificationCode);

        if (recaptchaResponse is { Success: true })
        {
            return new VerificationResult
            {
                Verified = true,
                HostName = recaptchaResponse.HostName ?? string.Empty
            };
        }

        return new VerificationResult
        {
            Verified = false,
            Errors = new Dictionary<string, string>
            {
                { "VerificationCode", "Verification code incorrect" }
            }
        };
    }

    private async Task<RecaptchaResponse> VerifyCaptchaAsync(string token)
    {
        try
        {
            var requestUri = await ResolveVerificationUri(token);
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var response = await _httpClientService.SendAsync(httpRequestMessage, LogContentType);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "reCAPTCHA siteverify call failed. Status: {StatusCode}",
                    response.StatusCode);
                return new RecaptchaResponse { Success = false };
            }

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            var recaptchaResponse = await JsonSerializer.DeserializeAsync<RecaptchaResponse>(
                contentStream,
                JsonOptions.Default);

            return recaptchaResponse ?? new RecaptchaResponse { Success = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "reCAPTCHA verification failed");
            return new RecaptchaResponse { Success = false };
        }
    }

    private async Task<string> ResolveVerificationUri(string token)
    {
        try
        {
            var config = await _recaptchaConfigFactory.GetRecaptchaConfig(_recaptchaVerificationUrl, token);
            return config.ResolveRecaptchaUri();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build reCAPTCHA verification URI; using default endpoint");
            return $"{_recaptchaVerificationUrl}?response={Uri.EscapeDataString(token)}";
        }
    }
}
