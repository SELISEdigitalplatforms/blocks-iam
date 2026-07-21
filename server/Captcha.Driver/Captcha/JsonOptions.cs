using System.Text.Json;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for captcha payloads.
/// </summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
