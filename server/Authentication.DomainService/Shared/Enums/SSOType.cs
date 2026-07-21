namespace Authentication.DomainService.Shared
{
    /// <summary>
    /// Kind of single sign-on integration that the application is configured
    /// for. Drives the OAuth flow used at the /authorize endpoint and the
    /// claims-mapping profile applied to inbound tokens.
    /// </summary>
    public enum SSOType
    {
        /// <summary>
        /// Hosted social identity providers (Google, Facebook, GitHub, etc.).
        /// The platform owns the client registration; the customer only picks
        /// which providers to enable.
        /// </summary>
        Social,

        /// <summary>
        /// Bring-your-own SSO: the customer registers their own OIDC/SAML
        /// identity provider. The platform acts as a relying party.
        /// </summary>
        BYOSSO
    }
}