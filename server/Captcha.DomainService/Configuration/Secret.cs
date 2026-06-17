using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Captcha.DomainService.Configuration
{
    /// <summary>
    /// Represents a document in the shared "Secret" collection. Different kinds of secrets
    /// are distinguished by <see cref="SecretKey"/> (e.g. "captcha"), with the actual values
    /// stored as string entries inside <see cref="KeyValuePairs"/>.
    /// </summary>
    public class Secret : BaseEntity
    {
        [BsonElement("SecretKey")]
        public string SecretKey { get; set; } = string.Empty;

        [BsonElement("KeyValuePairs")]
        public Dictionary<string, string> KeyValuePairs { get; set; }

        [BsonElement("KeyPairs")]
        public Dictionary<string, string> KeyPairs { get; set; }
    }

    /// <summary>
    /// Well-known keys used by captcha secrets stored in the "Secret" collection.
    /// </summary>
    public static class CaptchaSecretKeys
    {
        public const string SecretKey = "captcha";

        public const string IsEnable = "isEnable";
        public const string Provider = "provider";
        public const string CaptchaKey = "captchaKey";
        public const string CaptchaSecret = "captchaSecret";
        public const string CaptchaGenerator = "captchaGenerator";
    }
}
