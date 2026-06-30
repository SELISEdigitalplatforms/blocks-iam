using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Blocks.CaptchaDriver
{
    [BsonIgnoreExtraElements]
    public class Secret : BaseEntity
    {
        [BsonElement("SecretKey")]
        public string SecretKey { get; set; } = string.Empty;

        [BsonElement("KeyValuePairs")]
        public Dictionary<string, string> KeyValuePairs { get; set; }

        [BsonElement("KeyPairs")]
        public Dictionary<string, string> KeyPairs { get; set; }
    }

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