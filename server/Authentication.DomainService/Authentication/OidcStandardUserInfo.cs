using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Builds the OpenID Connect UserInfo response (OpenID Connect Core 1.0 §5.3.2): standard claims
    /// (§5.1) only, released per scope (§5.4). Blocks-specific data (tenant, organization, roles,
    /// permissions) stays on <c>auth/me</c> and is never handed to a relying party from here.
    /// A claim IAM has no reliable value for is left out rather than sent empty or guessed.
    /// </summary>
    public static class OidcStandardUserInfo
    {
        public const string OpenIdScope = "openid";
        public const string ProfileScope = "profile";
        public const string EmailScope = "email";
        public const string PhoneScope = "phone";

        /// <summary>
        /// Scopes assumed when the access token carries no <c>scope</c> claim, which IAM access tokens
        /// do not today: the same profile and email data the ID token already carries, never phone.
        /// </summary>
        public static readonly IReadOnlySet<string> DefaultScopes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { OpenIdScope, ProfileScope, EmailScope };

        public static Dictionary<string, object> Build(User user, string subject, IReadOnlySet<string> grantedScopes)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentException.ThrowIfNullOrWhiteSpace(subject);
            ArgumentNullException.ThrowIfNull(grantedScopes);

            // sub MUST match the ID token's sub (§5.3.2); both are the user id.
            var claims = new Dictionary<string, object> { ["sub"] = subject };

            if (grantedScopes.Contains(ProfileScope))
            {
                AddProfileClaims(claims, user);
            }

            if (grantedScopes.Contains(EmailScope))
            {
                AddEmailClaims(claims, user);
            }

            if (grantedScopes.Contains(PhoneScope))
            {
                AddPhoneClaims(claims, user);
            }

            return claims;
        }

        private static void AddProfileClaims(Dictionary<string, object> claims, User user)
        {
            // Same composition as the ID token's name claim (AuthorizationCodeExchangeService).
            var fullName = string.Join(' ', new[] { user.FirstName, user.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            AddIfPresent(claims, "name", fullName);
            AddIfPresent(claims, "given_name", user.FirstName);
            AddIfPresent(claims, "family_name", user.LastName);
            // Always set (defaults to the email at creation), so a user without a first or last
            // name still has a display name for the relying party.
            AddIfPresent(claims, "preferred_username", user.UserName);

            if (IsAbsoluteHttpUrl(user.ProfileImageUrl))
            {
                claims["picture"] = user.ProfileImageUrl!;
            }

            if (IsLanguageTag(user.Language))
            {
                claims["locale"] = user.Language!;
            }

            var updatedAt = ToUnixSeconds(user.LastUpdatedDate);
            if (updatedAt > 0)
            {
                claims["updated_at"] = updatedAt;
            }
        }

        private static void AddEmailClaims(Dictionary<string, object> claims, User user)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                return;
            }

            claims["email"] = user.Email;
            // Only a recorded verification proves it; a missing date is "unknown", not "unverified".
            if (user.EmailVerifiedAtUtc.HasValue)
            {
                claims["email_verified"] = true;
            }
        }

        private static void AddPhoneClaims(Dictionary<string, object> claims, User user)
        {
            if (string.IsNullOrWhiteSpace(user.PhoneNumber))
            {
                return;
            }

            claims["phone_number"] = user.PhoneNumber;
            if (user.PhoneVerifiedAtUtc.HasValue)
            {
                claims["phone_number_verified"] = true;
            }
        }

        private static void AddIfPresent(Dictionary<string, object> claims, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                claims[name] = value;
            }
        }

        private static bool IsAbsoluteHttpUrl(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

        /// <summary>BCP 47 shape (for example <c>en</c>, <c>en-US</c>, <c>zh-Hant-TW</c>).</summary>
        private static bool IsLanguageTag(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var parts = value.Split('-');
            if (parts[0].Length is < 2 or > 3 || !parts[0].All(char.IsAsciiLetter))
            {
                return false;
            }

            return parts.Skip(1).All(p => p.Length is >= 1 and <= 8 && p.All(char.IsAsciiLetterOrDigit));
        }

        private static long ToUnixSeconds(DateTime value)
        {
            if (value == default)
            {
                return 0;
            }

            var utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };

            return Math.Max(0, new DateTimeOffset(utc).ToUnixTimeSeconds());
        }
    }
}
