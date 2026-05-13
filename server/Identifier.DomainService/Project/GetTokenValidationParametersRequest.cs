using Blocks.Genesis;

namespace Identifier.DomainService.Projects
{
    public class GetTokenValidationParametersRequest : IProjectKey
    {
        public string ProjectKey { get; set; }
    }
}
