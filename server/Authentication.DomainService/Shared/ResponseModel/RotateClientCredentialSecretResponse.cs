using Blocks.Genesis;

namespace Authentication.DomainService.Shared.ResponseModel
{
    /// <summary>One-time disclosure of a newly rotated client-credential secret.</summary>
    public class RotateClientCredentialSecretResponse : BaseResponse
    {
        public string? ClientSecret { get; set; }
    }
}
