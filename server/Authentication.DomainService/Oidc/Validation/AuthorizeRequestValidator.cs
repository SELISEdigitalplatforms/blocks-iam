using Authentication.DomainService.OAuth.RequestModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Authentication.DomainService.Oidc.Validation
{
    public class AuthorizeRequestValidator
    {
        public AuthorizeValidationResult Validate(AuthorizeRequest request)
        {
            var errors = new List<string>();

            // client_id required
            if (string.IsNullOrWhiteSpace(request.ClientId))
                errors.Add("client_id is required");

            // response_type must be "code"
            if (string.IsNullOrWhiteSpace(request.ResponseType))
                errors.Add("response_type is required");
            else if (request.ResponseType != "code")
                errors.Add("response_type must be 'code'");

            // redirect_uri required
            if (string.IsNullOrWhiteSpace(request.RedirectUri))
                errors.Add("redirect_uri is required");

            // scope required and must contain "openid"
            if (string.IsNullOrWhiteSpace(request.Scope))
                errors.Add("scope is required");
            else if (!request.Scope
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("openid", StringComparer.Ordinal))
                errors.Add("scope must include 'openid'");

            // nonce is recommended for authorization code flow and required for implicit/hybrid.
            // This endpoint supports code flow only, so we do not reject missing nonce.

            // state is strongly recommended (CSRF prevention), but not mandatory.

            // RFC 7636: if code_challenge is present, method must be present and supported.
            if (!string.IsNullOrWhiteSpace(request.CodeChallenge) && !ValidatePkceFormat(request.CodeChallenge))
                errors.Add("code_challenge has invalid format");

            if (!string.IsNullOrWhiteSpace(request.CodeChallenge) && string.IsNullOrWhiteSpace(request.CodeChallengeMethod))
                errors.Add("code_challenge_method is required when code_challenge is provided");
            else if (!string.IsNullOrWhiteSpace(request.CodeChallengeMethod) && request.CodeChallengeMethod != "S256")
                errors.Add("code_challenge_method must be 'S256' (plain method not supported)");

            return new AuthorizeValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors
            };
        }

        private bool ValidatePkceFormat(string challenge)
        {
            // RFC 7636: code_challenge must be 43-128 BASE64URL characters
            if (string.IsNullOrEmpty(challenge) || challenge.Length < 43 || challenge.Length > 128)
                return false;

            // BASE64URL characters for S256 challenge output.
            var validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
            return challenge.All(c => validChars.Contains(c));
        }
    }

    public class AuthorizeValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}

