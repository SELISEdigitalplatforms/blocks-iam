using System.Text.Json.Serialization;

namespace Iam.DomainService.Accounts
{
    /// <summary>
    /// What an activation code turned out to be. The activation page routes on this: only
    /// <see cref="Valid"/> may be activated, and the other three each deserve their own message
    /// rather than the single "invalid link" they used to collapse into.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ActivationCodeStatus
    {
        /// <summary>No such code was ever issued -- a mistyped or fabricated link.</summary>
        Invalid,

        /// <summary>The code was issued but its lifetime lapsed before anyone used it. Resendable.</summary>
        Expired,

        /// <summary>
        /// The code belongs to an account that is already active. Reached when an email security
        /// scanner or link preview follows the link before the user does, and the account activates
        /// without a password step; the person who then clicks is looking at a finished job.
        /// </summary>
        AlreadyActivated,

        /// <summary>Issued, unused and still within its lifetime.</summary>
        Valid
    }
}
