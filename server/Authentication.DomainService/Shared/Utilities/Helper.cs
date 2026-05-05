using Authentication.DomainService.Entities;
using Authentication.DomainService.OAuth.RequestModel;
using HandlebarsDotNet;

namespace Authentication.DomainService.Utilities
{
    public static class Helper
    {
        public static string LoadAuthorizationHtmlContent(string templateContent, string loginUrl, string apiKey, string username, AuthorizeRequest request, OidcClientRegistration clientCredential)
        {
            var redirectUri = clientCredential.RedirectUris.FirstOrDefault() ?? string.Empty;
            
            // OIDC: Validate requested scopes against allowed scopes
            var requestedScopes = string.IsNullOrWhiteSpace(request.Scope) 
                ? [] 
                : request.Scope.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            
            var grantedScopes = requestedScopes.Length == 0 
                ? clientCredential.AllowedScopes 
                : requestedScopes.Intersect(clientCredential.AllowedScopes, StringComparer.OrdinalIgnoreCase).ToList();
            
            var scope = grantedScopes.Count == 0 ? string.Empty : string.Join(' ', grantedScopes);
            
            var model = new
            {
                response_type = "code",
                client_id = string.IsNullOrWhiteSpace(clientCredential.ClientId) ? clientCredential.ItemId : clientCredential.ClientId,
                redirect_uri = redirectUri,
                Scope = scope,
                State = request.State,
                Nonce = request.Nonce,
                LoginEndpointUrl = loginUrl,
                AcknowledgeUri = loginUrl,
                XBlocksKey = apiKey,
                Username = username,
                user = username
            };

            var template = Handlebars.Compile(templateContent);

            // Render with model values
            return template(model);
        }

        public static string GetAuthorizationError(string errorPageUri, AuthorizeRequest request, OidcClientRegistration clientCredential)
        {
            var redirectUriMismatch = clientCredential == null
                || string.IsNullOrWhiteSpace(request.RedirectUri)
                || !clientCredential.RedirectUris.Contains(request.RedirectUri, StringComparer.OrdinalIgnoreCase);

            var requestedScopes = string.IsNullOrWhiteSpace(request.Scope)
                ? []
                : request.Scope.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var allowedScopes = clientCredential?.AllowedScopes ?? [];
            var scopeMismatch = clientCredential == null
                || requestedScopes.Length == 0
                || requestedScopes.Except(allowedScopes, StringComparer.OrdinalIgnoreCase).Any();

            var validationRules = new (Func<bool> condition, string title, string code, string message)[]
                                  {
                                        (() => clientCredential == null,
                                         "Client not found",
                                         "invalid_client",
                                         $"no client exist with clientId {request.ClientId}"),

                                        (() => redirectUriMismatch,
                                         "Redirect URI mismatch",
                                         "invalid_request",
                                         $"The redirect_uri {request.RedirectUri} does not match the registered redirect URI."),

                                        (() => scopeMismatch,
                                         "Scope mismatch",
                                         "invalid_scope",
                                         $"The scope {request.Scope} does not match the registered scope.")
                                   };

            var (condition, title, code, message) = validationRules.FirstOrDefault(rule => rule.condition());

            return condition != null
                ? $"{errorPageUri}?title={title}&code={code}&messge={message}":
                errorPageUri;
        }
    }
}
