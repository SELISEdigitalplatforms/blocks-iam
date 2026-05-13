using System.Security.Cryptography.X509Certificates;

namespace Identifier.DomainService.Certificate
{
    public interface ICertificateStorage
    {
        Task UploadCertificateAsync(X509Certificate2 certificate, string password, string certificateName);
    }
}
