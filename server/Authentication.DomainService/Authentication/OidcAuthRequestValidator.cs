using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Shared;
using Iam.DomainService.Utilities;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Validates <see cref="AuthorizeRequest"/> values for the OIDC authorization endpoint
    /// and the PKCE <c>code_challenge</c> format (RFC 7636).
    /// Pure / stateless — extracted from <c>AuthorizationFlowService</c>.
    /// </summary>
    internal static class OidcAuthRequestValidator
    {
        public static AuthorizeValidationResult Validate(AuthorizeRequest request, bool isDeviceFlowClient = false)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(request.ClientId))
            {
                errors.Add("client_id is required");
            }

            if (string.IsNullOrWhiteSpace(request.ResponseType))
            {
                errors.Add("response_type is required");
            }
            else if (request.ResponseType != "code")
            {
                errors.Add("response_type must be 'code'");
            }

            // Device-flow clients (RFC 8628) have no browser redirect step, so redirect_uri
            // doesn't apply to them — skip this rule when the caller identifies one.
            if (!isDeviceFlowClient && string.IsNullOrWhiteSpace(request.RedirectUri))
            {
                errors.Add("redirect_uri is required");
            }

            if (string.IsNullOrWhiteSpace(request.Scope))
            {
                errors.Add("scope is required");
            }
            else if (!request.Scope
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("openid", StringComparer.Ordinal))
            {
                errors.Add("scope must include 'openid'");
            }

            if (!string.IsNullOrWhiteSpace(request.CodeChallenge) && !ValidatePkceFormat(request.CodeChallenge))
            {
                errors.Add("code_challenge has invalid format");
            }

            if (!string.IsNullOrWhiteSpace(request.CodeChallenge) && string.IsNullOrWhiteSpace(request.CodeChallengeMethod))
            {
                errors.Add("code_challenge_method is required when code_challenge is provided");
            }
            else if (!string.IsNullOrWhiteSpace(request.CodeChallengeMethod) && request.CodeChallengeMethod != IdpConstants.PkceMethodS256)
            {
                errors.Add($"code_challenge_method must be '{IdpConstants.PkceMethodS256}' (plain method not supported)");
            }

            return new AuthorizeValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors
            };
        }

        public static bool ValidatePkceFormat(string challenge)
        {
            // RFC 7636: code_challenge must be 43-128 BASE64URL characters.
            if (string.IsNullOrEmpty(challenge) || challenge.Length < 43 || challenge.Length > 128)
            {
                return false;
            }

            const string validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            return challenge.All(c => validChars.Contains(c));
        }
    }
}
