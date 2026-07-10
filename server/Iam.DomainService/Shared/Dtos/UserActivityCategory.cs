using System.Text.Json.Serialization;

namespace Iam.DomainService.Dtos
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UserActivityCategory
    {
        Account,
        Auth,
        Resource,
        Audit
    }
}