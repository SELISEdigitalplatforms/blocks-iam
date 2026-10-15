using System.Security.Cryptography;
using System.Text.Json;
using Iam.DomainService.Entities;
using Blocks.Genesis.Entities.Iam;

namespace Mfa.DomainService.Services
{
    public class MfaAuthenticationContext
    {
        public string? UserId { get; set; }

        public string? MfaId { get; set; }

        public string? MfaCode { get; set; }

        // The OTP *service* that owns this challenge (Email for code-based delivery, TOTP for
        // authenticator-app). Persisted so a later resend can resolve the method from the
        // mfa_id alone — and reject methods (TOTP) that have no code to resend.
        public UserMfaType MfaType { get; set; }

        // When the current code was last delivered. Drives the resend cooldown.
        public DateTime LastSentUtc { get; set; }

        // When the challenge itself dies. Set once at creation and never moved by a resend, so
        // resending cannot keep a challenge alive past its original window. Default (unset) on a
        // challenge written before this field existed; the resend path then derives it.
        public DateTime ExpiresAtUtc { get; set; }

        // Non-empty only when the code is delivered as an SMS via the phone->email gateway.
        // Persisted so a resend re-routes to SMS without the caller re-supplying the domain.
        public string? SendPhoneNumberAsEmailDomain { get; set; }

        public static MfaAuthenticationContext Create(string mfaId, string userId, UserMfaType mfaType)
        {
            return new MfaAuthenticationContext
            {
                UserId = userId,
                MfaId = mfaId,
                MfaCode = GenerateRandomAccessCode(),
                MfaType = mfaType,
                LastSentUtc = DateTime.UtcNow
            };
        }

        private static string GenerateRandomAccessCode()
        {
            return GenerateSecureRandomNumber();
        }

        public static string GenerateSecureRandomNumber()
        {
            // GetInt32 is uniform over the range (no modulo bias); "D6" keeps leading zeros.
            return RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        }

        public string Sterilize()
        {
            return JsonSerializer.Serialize(this);
        }

        public static MfaAuthenticationContext Deserialize(string json)
        {
            return JsonSerializer.Deserialize<MfaAuthenticationContext>(json)
                ?? throw new ArgumentException("Invalid MfaAuthenticationContext payload", nameof(json));
        }

        /// <summary>
        /// Safe parse used by the resend path. Returns false for a value that is not a
        /// code-based challenge context — notably a TOTP session, which stores a bare user id
        /// string (not this JSON shape) and therefore has nothing to resend.
        /// </summary>
        public static bool TryDeserialize(string? json, out MfaAuthenticationContext context)
        {
            context = new MfaAuthenticationContext();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<MfaAuthenticationContext>(json);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.MfaCode))
                {
                    return false;
                }

                context = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
