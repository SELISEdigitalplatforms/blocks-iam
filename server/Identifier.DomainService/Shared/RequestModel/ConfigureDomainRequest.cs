
using Blocks.Genesis;

namespace Identifier.DomainService.Shared
{
    public class ConfigureDomainRequest : IProjectKey
    {
        public string ProjectKey { get; set; }
        public string CookieDomain { get; set; }
    }
}
