using System.Text.RegularExpressions;
using Iam.DomainService.Dtos;

namespace Iam.DomainService.Configurations
{
    /// <summary>
    /// The single answer to "does this password satisfy the tenant's rule", shared by every
    /// validator that guards a password. The structured policy and the legacy regex are
    /// alternatives rather than layers, and that precedence is easy to get wrong when each
    /// validator carries its own copy -- so it is written once, here.
    /// </summary>
    public static class PasswordStrengthEvaluator
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

        public static bool IsStrongPassword(IamConfiguration? config, string password)
        {
            if (config == null)
                return true;

            if (string.IsNullOrWhiteSpace(config.PasswordStrengthCheckerRegex))
                return true;

            try
            {
                return Regex.IsMatch(password, config.PasswordStrengthCheckerRegex, RegexOptions.IgnoreCase, RegexTimeout);
            }
            catch (RegexMatchTimeoutException)
            {
                return false; // Consider the password invalid if the regex check times out
            }
        }
    }
}
