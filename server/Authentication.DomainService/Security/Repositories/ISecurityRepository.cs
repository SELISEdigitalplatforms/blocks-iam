using Authentication.DomainService.Security.Models;

namespace Authentication.DomainService.Security.Repositories
{
    /// <summary>
    /// Internal shape used by the repository to surface one rotation row for a
    /// refresh-token session. Never serialized: kept in <see cref="Repositories"/>
    /// so it does not leak into the wire contracts.
    /// </summary>
    public sealed class SessionRotationRecord
    {
        public string? ClientId { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime AbsoluteExpiry { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? RevokeReason { get; set; }
        public bool IsCurrent { get; set; }
    }

    public interface ISecurityRepository
    {
        Task<IReadOnlyList<UserSessionDto>> GetUserSessionsAsync(
            string userId,
            string? clientId,
            bool activeOnly,
            CancellationToken ct);

        Task<UserSessionDto?> GetUserSessionAsync(
            string userId,
            string sessionId,
            CancellationToken ct);

        Task<IReadOnlyList<SessionRotationRecord>> GetRotationHistoryAsync(
            string sessionId,
            CancellationToken ct);

        Task EnsureIndexesAsync(CancellationToken ct);
    }
}
