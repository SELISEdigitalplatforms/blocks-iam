using System.Text.RegularExpressions;

namespace Iam.DomainService.Users
{
    /// <summary>
    /// Pure rules shared by users/create, users/access and the batch upsert, so the single-user
    /// routes and the batch can never disagree on what a valid email or a permission cap is.
    /// </summary>
    public static class UserIdentityRules
    {
        /// <summary>Direct permissions one user may hold in one organization.</summary>
        public const int MaxPermissionsPerUser = 5;

        private const string EmailPattern =
            @"\A(?:[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)\Z";

        /// <summary>The email format users/create has always accepted.</summary>
        public static bool IsValidEmail(string? email) =>
            !string.IsNullOrEmpty(email)
            && Regex.IsMatch(email, EmailPattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));

        /// <summary>Emails are stored and matched trimmed and lower-case.</summary>
        public static string NormalizeEmail(string? email) =>
            string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();
    }
}
