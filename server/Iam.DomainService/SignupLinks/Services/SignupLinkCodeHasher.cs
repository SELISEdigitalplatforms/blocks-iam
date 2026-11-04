using System.Security.Cryptography;
using System.Text;

namespace Iam.DomainService.SignupLinks;

public static class SignupLinkCodeHasher
{
    public static string Hash(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }

        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    }

    /// <summary>
    /// First two characters of the local part, then bullets, then @domain.
    /// </summary>
    public static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1)
        {
            return "•••";
        }

        var local = email[..at];
        var domain = email[(at + 1)..];
        var prefix = local.Length >= 2 ? local[..2] : local;
        return $"{prefix}•••@{domain}";
    }
}
