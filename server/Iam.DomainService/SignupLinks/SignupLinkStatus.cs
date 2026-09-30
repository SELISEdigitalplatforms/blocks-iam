using System.Text.Json.Serialization;

namespace Iam.DomainService.SignupLinks;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SignupLinkStatus
{
    Active = 0,
    Redeemed = 1,
    Exhausted = 2,
    Revoked = 3,
    Expired = 4
}
