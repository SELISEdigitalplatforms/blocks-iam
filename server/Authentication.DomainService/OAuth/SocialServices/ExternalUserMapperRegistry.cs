using System.Text.Json;

namespace Authentication.DomainService.OAuth.SocialServices
{
    public sealed class ExternalUserMapperRegistry : IExternalUserMapperRegistry
    {
        private readonly Dictionary<string, IExternalUserMapper> _mappers;
        private readonly IExternalUserMapper _fallback;

        public ExternalUserMapperRegistry(IEnumerable<IExternalUserMapper> mappers)
        {
            _mappers = mappers
                .Where(m => m.ProviderKey != "__generic__")
                .ToDictionary(m => m.ProviderKey, StringComparer.OrdinalIgnoreCase);
            _fallback = mappers.FirstOrDefault(m => m.ProviderKey == "__generic__")
                ?? new GenericOidcExternalUserMapper();
        }

        public void Map(string provider, JsonElement result, BYOSsoUserData user)
        {
            if (_mappers.TryGetValue(provider, out var mapper))
            {
                mapper.Map(result, user);
                return;
            }

            _fallback.Map(result, user);
        }
    }
}
