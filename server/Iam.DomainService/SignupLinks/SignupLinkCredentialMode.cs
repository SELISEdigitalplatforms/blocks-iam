using System.Text.Json.Serialization;

namespace Iam.DomainService.SignupLinks;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SignupLinkCredentialMode
{
    Passwordless = 0,
    PasswordRequired = 1
}
