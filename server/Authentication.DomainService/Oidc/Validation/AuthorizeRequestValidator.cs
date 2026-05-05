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
            else if (!request.Scope.Contains("openid"))
                errors.Add("scope must include 'openid'");

            // nonce required (OIDC Core 1.0)
            if (string.IsNullOrWhiteSpace(request.Nonce))
                errors.Add("nonce is required");

            // state recommended (CSRF prevention)
            if (string.IsNullOrWhiteSpace(request.State))
                errors.Add("state is recommended");

            // code_challenge required (PKCE RFC 7636)
            if (string.IsNullOrWhiteSpace(request.CodeChallenge))
                errors.Add("code_challenge is required (PKCE)");
            else if (!ValidatePkceFormat(request.CodeChallenge))
                errors.Add("code_challenge has invalid format");

            // code_challenge_method must be S256
            if (string.IsNullOrWhiteSpace(request.CodeChallengeMethod))
                errors.Add("code_challenge_method is required");
            else if (request.CodeChallengeMethod != "S256")
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

            // Check valid BASE64URL characters (A-Z, a-z, 0-9, -, ., _, ~)
            var validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
            return challenge.All(c => validChars.Contains(c));
        }
    }

    public class AuthorizeValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}

