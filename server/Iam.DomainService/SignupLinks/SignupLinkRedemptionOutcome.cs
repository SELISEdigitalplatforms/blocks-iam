using System.Text.Json.Serialization;

namespace Iam.DomainService.SignupLinks;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SignupLinkRedemptionOutcome
{
    UserCreated = 0,
    Rejected = 1,
    LinkUserReturned = 2,
    OrganizationJoined = 3,
    ExistingUserRedirected = 4,

    /// <summary>
    /// Redeem answered an already-active user with a password step. Nothing was granted or
    /// consumed, and it is not a rejection.
    /// </summary>
    AuthenticationRequired = 5
}
