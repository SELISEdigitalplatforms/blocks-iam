using Blocks.Genesis;

namespace Authentication.DomainService.Shared.ResponseModel
{
    /// <summary>
    /// Result of creating or updating a client credential. A secret is disclosed only when a
    /// credential is newly created; updates preserve their existing secret.
    /// </summary>
    public class SaveClientCredentialResponse : BaseResponse
    {
        public string? ItemId { get; set; }
        public string? ClientSecret { get; set; }
    }
}
