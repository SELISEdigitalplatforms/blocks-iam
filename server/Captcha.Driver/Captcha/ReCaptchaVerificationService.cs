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

            if (string.IsNullOrWhiteSpace(requestUri))
            {
                // The tenant is configured but its secret could not be resolved. Calling Google
                // without it would be pointless and would leak the token to an unverifiable call.
                _logger.LogError("reCAPTCHA verification skipped: no usable secret for this tenant.");
                return new RecaptchaResponse { Success = false };
            }

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

    /// <summary>
    /// Builds the siteverify URI, or returns <c>null</c> when the tenant's secret is unavailable.
    /// </summary>
    private async Task<string?> ResolveVerificationUri(string token)
    {
        try
        {
            var config = await _recaptchaConfigFactory.GetRecaptchaConfig(_recaptchaVerificationUrl, token);
            return config?.ResolveRecaptchaUri();
        }
        catch (Exception ex)
        {
            // Fail closed rather than retrying against the default endpoint: that endpoint carries
            // no secret, so the call could only ever come back unverified anyway.
            _logger.LogError(ex, "Failed to build the reCAPTCHA verification URI.");
            return null;
        }
    }
}
