using Blocks.Genesis;

namespace Identifier.DomainService.Certificate
{
    public interface ICertificateStorageFactory
    {
        ICertificateStorage Create(CertificateStorageType storageType);
    }
}
