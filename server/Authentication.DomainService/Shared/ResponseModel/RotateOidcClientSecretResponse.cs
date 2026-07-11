using Blocks.Genesis;

namespace Authentication.DomainService.Shared.ResponseModel
{
    public class RotateOidcClientSecretResponse : BaseResponse
    {
        public string? ItemId { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
        public DateTime? RotatedAt { get; set; }
        public string? RotatedBy { get; set; }
    }
}
