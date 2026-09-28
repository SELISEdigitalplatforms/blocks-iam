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
                if (!TryResolveUnderBaseDirectory(key, out var path))
                {
                    _logger.LogError("Certificate key must resolve under the app base directory");
                    return Array.Empty<byte>();
                }

                return await File.ReadAllBytesAsync(path);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error retrieving certificate from file system");
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Resolves <paramref name="key"/> under <see cref="AppContext.BaseDirectory"/> without
        /// <c>Path.Combine</c> (which static analysis cannot prove is bounded). Rejects rooted
        /// keys and any <c>..</c> segment, then verifies the full path still sits under the root.
        /// </summary>
        private static bool TryResolveUnderBaseDirectory(string key, out string fullPath)
        {
            fullPath = string.Empty;
            var root = Path.GetFullPath(AppContext.BaseDirectory);
            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (Path.IsPathRooted(key))
            {
                return false;
            }

            var segments = key.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.None);
            if (segments.Length == 0 || segments.Any(static s => s is ".." or ""))
            {
                return false;
            }

            var relative = string.Join(Path.DirectorySeparatorChar, segments);
            var candidate = Path.GetFullPath(rootPrefix + relative);
            if (!candidate.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
    }
}
