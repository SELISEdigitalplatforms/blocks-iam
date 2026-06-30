
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.OAuth
{
    public sealed class FileSystemCertificateProvider : ICertificateProvider
    {
        private readonly ILogger _logger;

        public FileSystemCertificateProvider(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<byte[]> GetCertificateAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                _logger.LogError("Certificate key is required for file-system provider");
                return Array.Empty<byte>();
            }

            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, key);
                return await File.ReadAllBytesAsync(path);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error retrieving certificate from file system");
                return Array.Empty<byte>();
            }
        }
    }
}
