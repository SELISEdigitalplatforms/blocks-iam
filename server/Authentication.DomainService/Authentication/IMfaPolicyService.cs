using Iam.DomainService.Entities;
using Mfa.DomainService.Services;

namespace Authentication.DomainService.Authentication
{
    public interface IMfaPolicyService
    {
        Task<MfaPolicyDecision> EvaluateAsync(User user, string? clientId, CancellationToken cancellationToken = default);
    }

    public class MfaPolicyDecision
    {
        public bool Required { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<UserMfaType> AllowedMethods { get; set; } = [];
        public UserMfaType? PreferredMethod { get; set; }
        public bool CanUserDisable { get; set; } = true;
        public bool MustEnrollFirst { get; set; }
    }
}
