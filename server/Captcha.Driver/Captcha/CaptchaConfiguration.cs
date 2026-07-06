using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Persisted captcha configuration. Mirrors a document in the <c>Secrets</c> collection
/// with secret key <see cref="CaptchaSecretKeys.SecretKey"/>.
/// </summary>
[BsonIgnoreExtraElements]
public class CaptchaConfiguration : BaseEntity
{
    /// <summary>Site key (public) presented to the browser.</summary>
    public string CaptchaKey { get; set; } = string.Empty;

    /// <summary>Server-side secret used to verify tokens.</summary>
    public string CaptchaSecret { get; set; } = string.Empty;

    /// <summary>Provider identifier (e.g. <c>recaptcha</c>, <c>hcaptcha</c>, <c>bcaptcha</c>).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Generator identifier (optional).</summary>
    public string CaptchaGenerator { get; set; } = string.Empty;

    /// <summary>Whether this configuration is currently active.</summary>
    public bool IsEnable { get; set; }
}
