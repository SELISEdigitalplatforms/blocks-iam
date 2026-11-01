using Blocks.Genesis.Entities.Iam;
using Microsoft.AspNetCore.Http;

namespace Authentication.DomainService.Authentication
{
    public interface IMfaDisableService
    {
        Task<MfaDisableResult> DisableAsync(string userId, MfaDisableRequest request);
    }

    /// <summary>
    /// Proof for turning MFA off: the password, and a code for a challenge from
    /// <c>POST /mfa/generate</c> (the user's current method).
    /// </summary>
    public sealed class MfaDisableRequest
    {
        public string? Password { get; set; }
        public string? MfaId { get; set; }
        public string? VerificationCode { get; set; }
    }

    public sealed record MfaDisableResult(bool Succeeded, int StatusCode, string? Error, string? ErrorDescription, UserMfaType PreviousMethod)
    {
        public static MfaDisableResult Success(UserMfaType previousMethod) =>
            new(true, StatusCodes.Status200OK, null, null, previousMethod);

        public static MfaDisableResult Fail(int statusCode, string error, string? description) =>
            new(false, statusCode, error, description, UserMfaType.None);
    }
}
