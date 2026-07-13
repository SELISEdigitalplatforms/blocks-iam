namespace Authentication.DomainService.OAuth
{
    /// <summary>
/// Discriminates between the two social-login callback shapes the platform
/// understands. Picked when the OAuth <c>state</c> parameter is constructed
/// and used again when the provider redirects back to the callback URL.
/// </summary>
public enum SocialFlowType
{
    /// <summary>Plain social login: code exchanged for a token, no PKCE, no OIDC <c>id_token</c> expected.</summary>
    Normal = 0,

    /// <summary>OIDC social login: PKCE-protected, <c>id_token</c> is validated and used to mint a session.</summary>
    Oidc = 1
}

    public record StateInfo
    {
        public required string ClientId { get; set; }
        public required string Provider { get; set; }
        public string? Code { get; set; }
        public string? NextUrl { get; set; }
        public required string Audience { get; set; }
        public string? State { get; set; }
        public string? Scope { get; set; }
        public string? UserName { get; set; }
        public string? Nonce { get; set; }
        public string? Secret { get; set; }
        public string? RedirectUri { get; set; }
        public Dictionary<string, string>? Extra { get; set; }
        public SocialFlowType FlowType { get; set; } = SocialFlowType.Normal;
    }
}
