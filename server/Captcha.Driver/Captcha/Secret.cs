using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Blocks.CaptchaDriver;

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
