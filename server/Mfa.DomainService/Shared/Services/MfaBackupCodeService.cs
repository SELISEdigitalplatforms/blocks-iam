using System.Security.Cryptography;
using System.Text;
using Mfa.DomainService.Shared;

namespace Mfa.DomainService.Services
{
    public interface IMfaBackupCodeService
    {
        Task<MfaBackupCodeGenerationResult> GenerateAsync(string userId, int count, CancellationToken cancellationToken = default);
        Task<MfaBackupCodeConsumeResult> ConsumeAsync(string userId, string code, CancellationToken cancellationToken = default);
        Task RevokeAllAsync(string userId, CancellationToken cancellationToken = default);
        Task<int> GetRemainingCountAsync(string userId, CancellationToken cancellationToken = default);
    }

    public class MfaBackupCodeGenerationResult
    {
        public bool IsSuccess { get; set; }
        public List<string> PlainCodes { get; set; } = [];
        public Dictionary<string, string> Errors { get; set; } = new();
    }

    public class MfaBackupCodeConsumeResult
    {
        public bool IsValid { get; set; }
        public string? UserId { get; set; }
        public Dictionary<string, string> Errors { get; set; } = new();
    }

    public class MfaBackupCodeService : IMfaBackupCodeService
    {
        private readonly IMfaManagementRepository _repository;

        public MfaBackupCodeService(IMfaManagementRepository repository)
        {
            _repository = repository;
        }

        public async Task<MfaBackupCodeGenerationResult> GenerateAsync(string userId, int count, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new MfaBackupCodeGenerationResult { Errors = new Dictionary<string, string> { { "empty_user_id", "userId is required" } } };
            }

            if (count <= 0 || count > 50)
            {
                return new MfaBackupCodeGenerationResult { Errors = new Dictionary<string, string> { { "invalid_count", "count must be between 1 and 50" } } };
            }

            await RevokeAllAsync(userId, cancellationToken);

            var plain = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var code = GeneratePlainCode();
                plain.Add(code);
                await _repository.SaveAsync(new MfaBackupCode
                {
                    UserId = userId,
                    CodeHash = HashCode(code),
                    CodePrefix = code[..Math.Min(4, code.Length)],
                    CreatedDate = DateTime.UtcNow,
                    LastUpdatedDate = DateTime.UtcNow,
                    CreatedBy = userId,
                    LastUpdatedBy = userId
                });
            }

            return new MfaBackupCodeGenerationResult { IsSuccess = true, PlainCodes = plain };
        }

        public async Task<MfaBackupCodeConsumeResult> ConsumeAsync(string userId, string code, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
            {
                return new MfaBackupCodeConsumeResult { Errors = new Dictionary<string, string> { { "invalid_request", "userId and code are required" } } };
            }

            var normalized = NormalizeCode(code);
            var hash = HashCode(normalized);

            var candidates = await _repository.GetItemsAsync<MfaBackupCode>(b => b.UserId == userId && !b.IsUsed);

            var match = candidates.FirstOrDefault(b => string.Equals(b.CodeHash, hash, StringComparison.Ordinal));
            if (match == null)
            {
                return new MfaBackupCodeConsumeResult { Errors = new Dictionary<string, string> { { "invalid_code", "Backup code is invalid or already used" } } };
            }

            match.IsUsed = true;
            match.UsedAtUtc = DateTime.UtcNow;
            match.LastUpdatedDate = DateTime.UtcNow;
            match.LastUpdatedBy = userId;
            await _repository.UpsertAsync(match, b => b.ItemId == match.ItemId);

            return new MfaBackupCodeConsumeResult { IsValid = true, UserId = userId };
        }

        public async Task RevokeAllAsync(string userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }
            await _repository.DeleteItemsAsync<MfaBackupCode>(b => b.UserId == userId);
        }

        public async Task<int> GetRemainingCountAsync(string userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return 0;
            }
            var codes = await _repository.GetItemsAsync<MfaBackupCode>(b => b.UserId == userId && !b.IsUsed);
            return codes.Count;
        }

        private static string GeneratePlainCode()
        {
            Span<byte> bytes = stackalloc byte[8];
            RandomNumberGenerator.Fill(bytes);
            var hex = Convert.ToHexString(bytes).ToLowerInvariant();
            return $"{hex[..4]}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}";
        }

        private static string NormalizeCode(string code)
        {
            return code.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
        }

        private static string HashCode(string code)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(code));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
