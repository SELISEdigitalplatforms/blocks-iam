using Blocks.Genesis;

namespace Authentication.DomainService.RequestModel
{
    public class GetSsoCredentialRequest : IProjectKey
    {
        public string? ProjectKey { get; set; }
        public string? ItemId { get; set; }
    }
}
