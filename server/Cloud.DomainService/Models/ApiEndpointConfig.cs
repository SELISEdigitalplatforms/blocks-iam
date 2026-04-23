using Blocks.Genesis;
using MongoDB.Bson.Serialization.Attributes;

namespace Cloud.DomainService.Models
{
    [BsonIgnoreExtraElements]
    public class ApiEndpointConfig : BaseEntity
    {
        [BsonElement("Service")]
        public string Service { get; set; } = string.Empty;

        [BsonElement("Method")]
        public string Method { get; set; } = string.Empty;

        [BsonElement("Endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        [BsonElement("Description")]
        public string? Description { get; set; }

        [BsonElement("IsCaptchaRequired")]
        public bool IsCaptchaRequired { get; set; }

        [BsonElement("CaptchaProvider")]
        public string? CaptchaProvider { get; set; }

        [BsonElement("IsMfaRequired")]
        public bool IsMfaRequired { get; set; }

        [BsonElement("MfaType")]
        public string? MfaType { get; set; }
        [BsonElement("Controller")]
        public string Controller { get; set; }
        [BsonElement("BaseUrl")]
        public string BaseUrl { get; set; }
        [BsonElement("Version")]
        public string Version { get; set; }
    }
}
