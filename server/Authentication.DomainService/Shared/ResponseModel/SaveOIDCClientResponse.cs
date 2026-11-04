using Blocks.Genesis;

namespace Authentication.DomainService.Shared.ResponseModel
{
    public class SaveOIDCClientResponse : BaseResponse
    {
        public string? ItemId { get; set; }
        /// <summary>Set on create only: the one time the generated secret is returned.</summary>
        public string? ClientSecret { get; set; }
    }
}
