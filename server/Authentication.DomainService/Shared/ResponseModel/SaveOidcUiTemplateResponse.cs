using Blocks.Genesis;

namespace Authentication.DomainService.Shared.ResponseModel
{
    /// <summary>Result of replacing a tenant's OIDC UI template.</summary>
    public sealed class SaveOidcUiTemplateResponse : BaseResponse
    {
        public string? ItemId { get; set; }
    }
}
