using Blocks.Genesis;

namespace Authentication.DomainService.OAuth
{
    public interface ICertificateProviderFactory
    {
        ICertificateProvider GetProvider(CertificateStorageType providerType);
    }
}
