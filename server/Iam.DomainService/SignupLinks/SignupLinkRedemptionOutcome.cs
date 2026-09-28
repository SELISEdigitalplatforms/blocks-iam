using System.Text.Json.Serialization;

namespace Iam.DomainService.SignupLinks;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SignupLinkRedemptionOutcome
{
    UserCreated = 0,
    Rejected = 1
}
