using Blocks.Genesis;


namespace Identifier.DomainService.Projects
{
    public class GetThirdPartyJWTClaimsRequest : IProjectKey
    {
        public string ProjectKey { get ; set ; }
        public string? ItemId { get; set; }
    }
}
