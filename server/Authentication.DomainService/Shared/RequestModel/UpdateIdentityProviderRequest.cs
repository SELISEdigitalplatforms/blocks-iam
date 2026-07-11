namespace Authentication.DomainService.Shared.RequestModel
{
    public sealed class UpdateIdentityProviderRequest
    {
        public string? Provider { get; set; }
        public string? ProviderType { get; set; }
        public string? Protocol { get; set; }
        public string? ClientId { get; set; }
        public string? DisplayName { get; set; }
        public bool? IsActive { get; set; }
        public string? Issuer { get; set; }
        public string? AuthorizationUrl { get; set; }
        public string? TokenUrl { get; set; }
        public string? UserInfoUrl { get; set; }
        public string? JwksUri { get; set; }
        public string? WellKnownUrl { get; set; }
        public List<string>? RedirectUris { get; set; }
        public string? Scope { get; set; }
        public string? ResponseType { get; set; }
        public List<string>? GrantTypes { get; set; }
        public bool? RequirePkce { get; set; }
        public string? TokenEndpointAuthMethod { get; set; }
        public List<string>? InitialRoles { get; set; }
        public List<string>? InitialPermissions { get; set; }
        public string? Icon { get; set; }
        public string? TeamId { get; set; }
        public string? KeyId { get; set; }
        public string? PrivateKey { get; set; }
        public string? AppleAudience { get; set; }
    }
}
