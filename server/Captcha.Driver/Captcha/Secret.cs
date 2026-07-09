using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Blocks.CaptchaDriver;

/// <summary>
/// MongoDB document representing a generic key/value secret. Captcha configuration
/// is stored in the <c>Secrets</c> collection under <see cref="CaptchaSecretKeys.SecretKey"/>.
/// </summary>
[BsonIgnoreExtraElements]
public class Secret : BaseEntity
{
    /// <summary>Secret identifier (e.g. <c>captcha</c>).</summary>
    [BsonElement("SecretKey")]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Free-form string key/value pairs.</summary>
    [BsonElement("KeyValuePairs")]
    public Dictionary<string, string> KeyValuePairs { get; set; } = new();

    /// <summary>Legacy alternative key/value map kept for backward compatibility.</summary>
    [BsonElement("KeyPairs")]
    public Dictionary<string, string> KeyPairs { get; set; } = new();
}

/// <summary>
/// Canonical key names used inside the captcha secret document's <c>KeyValuePairs</c> map.
/// </summary>
public static class CaptchaSecretKeys
{
    /// <summary>Secret document key (value of <see cref="Secret.SecretKey"/>).</summary>
    public const string SecretKey = "captcha";

    /// <summary>Boolean toggle enabling the configuration.</summary>
    public const string IsEnable = "isEnable";

    /// <summary>Provider identifier (e.g. <c>recaptcha</c>, <c>hcaptcha</c>).</summary>
    public const string Provider = "provider";

    /// <summary>Site key (public).</summary>
    public const string CaptchaKey = "captchaKey";

    /// <summary>Server-side secret.</summary>
    public const string CaptchaSecret = "captchaSecret";

    /// <summary>Captcha generator identifier.</summary>
    public const string CaptchaGenerator = "captchaGenerator";
}
