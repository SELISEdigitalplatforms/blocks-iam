using System.Security.Cryptography;
using System.Text;

namespace Authentication.DomainService.Oidc.Services
{
    /// <summary>
    /// Cryptographically secure generator for RFC 8628 device and user codes.
    /// <list type="bullet">
    /// <item><c>device_code</c> — 32 random bytes (256-bit) rendered as base64url. Sent only to the
    /// device over the backchannel; the raw value is never persisted.</item>
    /// <item><c>user_code</c> — 8 characters from an unambiguous alphabet (<c>BCDFGHJKMPQRTVWXY2346789</c>)
    /// grouped as <c>XXXX-XXXX</c> per RFC 8628 §6.1.</item>
    /// </list>
    /// </summary>
    public sealed class DeviceCodeGenerator
    {
        private const string UserCodeAlphabet = "BCDFGHJKMPQRTVWXY2346789";
        private const int UserCodeLength = 8;

        public string GenerateDeviceCode()
        {
            Span<byte> buffer = stackalloc byte[32];
            RandomNumberGenerator.Fill(buffer);
            return Base64UrlEncode(buffer);
        }

        public string GenerateUserCode()
        {
            var sb = new StringBuilder(UserCodeLength + 1);
            for (var i = 0; i < UserCodeLength; i++)
            {
                if (i == 4)
                {
                    sb.Append('-');
                }
                sb.Append(UserCodeAlphabet[RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)]);
            }
            return sb.ToString();
        }

        public string HashDeviceCode(string deviceCode)
        {
            if (string.IsNullOrEmpty(deviceCode))
            {
                throw new ArgumentException("deviceCode must not be empty", nameof(deviceCode));
            }

            var bytes = Encoding.ASCII.GetBytes(deviceCode);
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string Base64UrlEncode(ReadOnlySpan<byte> buffer)
        {
            var b64 = Convert.ToBase64String(buffer);
            return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}