using System.Security.Cryptography;

using Authentication.DomainService.Utilities;
namespace Authentication.DomainService.Utilities
{
    public static class ClientSecretGenerator
    {
        private const string Prefix = "blxk_";
        private const int SizeInBytes = 32;

        public static string Generate()
        {
            var buffer = new byte[SizeInBytes];
            RandomNumberGenerator.Fill(buffer);

            return Prefix + Convert.ToBase64String(buffer)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
