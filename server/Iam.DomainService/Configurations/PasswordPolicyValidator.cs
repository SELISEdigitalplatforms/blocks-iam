using Iam.DomainService.Dtos;

namespace Iam.DomainService.Configurations
{
    /// <summary>
    /// Save-time checks for the stored structured password policy fields, so a malformed bound
    /// can never be written. Nothing enforces or publishes those fields any more -- the tenant's
    /// <c>PasswordStrengthCheckerRegex</c> is the one rule, enforced by
    /// <see cref="PasswordStrengthEvaluator"/> and described to the screens by decoding it -- so
    /// <see cref="IsPasswordCompliant"/> currently has no caller.
    /// </summary>
    public static class PasswordPolicyValidator
    {
        public const int MinAllowedLength = 1;
        public const int MaxAllowedLength = 256;
        public const int MaxMessageLength = 500;

        /// <summary>Returns the first validation error, or an empty dictionary when the input is valid.</summary>
        public static IReadOnlyDictionary<string, string> ValidateAdminInput(int minLength, int maxLength, string? message)
        {
            if (minLength < MinAllowedLength || minLength > MaxAllowedLength)
            {
                return new Dictionary<string, string>
                {
                    { nameof(IamConfiguration.PasswordPolicyMinLength), "PasswordPolicyMinLength_Out_Of_Range" }
                };
            }

            if (maxLength > MaxAllowedLength || maxLength < minLength)
            {
                return new Dictionary<string, string>
                {
                    { nameof(IamConfiguration.PasswordPolicyMaxLength), "PasswordPolicyMaxLength_Out_Of_Range" }
                };
            }

            if ((message ?? string.Empty).Length > MaxMessageLength)
            {
                return new Dictionary<string, string>
                {
                    { nameof(IamConfiguration.PasswordPolicyMessage), "PasswordPolicyMessage_Too_Long" }
                };
            }

            return new Dictionary<string, string>();
        }

        /// <summary>
        /// ASCII-only classification, deliberately: a non-ASCII letter (e.g. an accented
        /// character) counts as neither uppercase, lowercase, nor a number here, so it is
        /// "special" -- the same rule a browser-side check must apply for the two to never
        /// disagree on a password's classification.
        /// </summary>
        public static bool IsPasswordCompliant(string? password, PasswordPolicySnapshot policy)
        {
            password ??= string.Empty;

            if (password.Length < policy.MinLength || password.Length > policy.MaxLength)
                return false;

            if (policy.RequireUppercase && !password.Any(IsAsciiUppercase))
                return false;

            if (policy.RequireLowercase && !password.Any(IsAsciiLowercase))
                return false;

            if (policy.RequireNumbers && !password.Any(IsAsciiDigit))
                return false;

            if (policy.RequireSpecialChars && !password.Any(IsAsciiSpecialChar))
                return false;

            return true;
        }

        private static bool IsAsciiUppercase(char c) => c is >= 'A' and <= 'Z';
        private static bool IsAsciiLowercase(char c) => c is >= 'a' and <= 'z';
        private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';
        private static bool IsAsciiSpecialChar(char c) => !IsAsciiUppercase(c) && !IsAsciiLowercase(c) && !IsAsciiDigit(c);
    }

    /// <summary>Immutable snapshot of the structured policy fields, read once per check.</summary>
    public readonly struct PasswordPolicySnapshot
    {
        public int MinLength { get; }
        public int MaxLength { get; }
        public bool RequireUppercase { get; }
        public bool RequireLowercase { get; }
        public bool RequireNumbers { get; }
        public bool RequireSpecialChars { get; }

        public PasswordPolicySnapshot(
            int minLength,
            int maxLength,
            bool requireUppercase,
            bool requireLowercase,
            bool requireNumbers,
            bool requireSpecialChars)
        {
            MinLength = minLength;
            MaxLength = maxLength;
            RequireUppercase = requireUppercase;
            RequireLowercase = requireLowercase;
            RequireNumbers = requireNumbers;
            RequireSpecialChars = requireSpecialChars;
        }

        public static PasswordPolicySnapshot From(IamConfiguration config) => new(
            config.PasswordPolicyMinLength,
            config.PasswordPolicyMaxLength,
            config.PasswordPolicyRequireUppercase,
            config.PasswordPolicyRequireLowercase,
            config.PasswordPolicyRequireNumbers,
            config.PasswordPolicyRequireSpecialChars);
    }
}
