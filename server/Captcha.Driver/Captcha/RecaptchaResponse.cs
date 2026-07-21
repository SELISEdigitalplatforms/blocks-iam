using System.Text.Json.Serialization;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Subset of the Google reCAPTCHA / hCaptcha siteverify response used by this driver.
/// </summary>
public class RecaptchaResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("hostname")]
    public string? HostName { get; set; }
}
