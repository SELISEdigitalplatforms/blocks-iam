namespace Authentication.DomainService.OAuth.ResponseModel
{
    /// <summary>
    /// Canonical body shape for every endpoint that issues OAuth tokens (login, refresh,
    /// switch-organization, impersonation). Different endpoints layer their own extra fields
    /// (e.g. impersonation_mode) on top of the dictionary this returns, but the token fields
    /// themselves — and whether access_token/refresh_token are omitted because they went into
    /// cookies instead — are built the same way everywhere so clients see one consistent shape.
    /// </summary>
    public static class TokenResponsePayload
    {
        public static Dictionary<string, object?> Build(TokenResponse response, bool cookiesSet)
        {
            var payload = new Dictionary<string, object?>
            {
                ["token_type"] = response.TokenType,
                ["expires_in"] = response.ExpiresIn,
                ["expires_utc"] = response.ExpiresUtc,
                ["refresh_expires_utc"] = response.RefreshExpiresUtc,
                ["scope"] = response.Scope,
                ["id_token"] = response.IdToken,
                ["cookie_set"] = cookiesSet
            };

            if (!cookiesSet)
            {
                payload["access_token"] = response.AccessToken;
                payload["refresh_token"] = response.RefreshToken;
            }

            return payload;
        }
    }
}
