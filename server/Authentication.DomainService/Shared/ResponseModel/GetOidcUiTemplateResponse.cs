using Authentication.DomainService.Entities;

namespace Authentication.DomainService.Shared.ResponseModel
{
    /// <summary>Tenant-level OIDC UI template management response.</summary>
    public sealed class GetOidcUiTemplateResponse
    {
        public OidcUiTemplate? Template { get; set; }
    }
}
