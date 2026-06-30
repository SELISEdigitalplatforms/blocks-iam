using Newtonsoft.Json;

namespace Blocks.CaptchaDriver
{
    public class RecaptchaResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }
        [JsonProperty("hostname")]
        public string HostName { get; set; }
    }
}