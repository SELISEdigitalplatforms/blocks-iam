using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Blocks.Genesis;


namespace Authentication.DomainService.Shared.Services
{
    public interface IImpersonationFlowHelper
    {
        Task<string> CreateAndBackupImpersonationSessionAsync(
            string userId,
            string rootTenantId,
            string targetTenantId,
            string clientId,
            string? organizationId);
        Task<bool> SwitchOrganizationContextAsync(string impersonationSessionId, string newOrganizationId);

    }

    public sealed class ImpersonationFlowHelper : IImpersonationFlowHelper
    {
        private readonly IAuthenticationRepository _repository;

        public ImpersonationFlowHelper(
            IAuthenticationRepository repository
        )
        {
            _repository = repository;
        }

        public async Task<bool> SwitchOrganizationContextAsync(
            string impersonationSessionId,
            string newOrganizationId)
        {
            var bc = BlocksContext.GetContext();
            var session = await _repository.GetImpersonationSessionByIdAsync(impersonationSessionId);
            if (session == null || session.Status != "active")
            {
                return false;
            }

            var updates = new Dictionary<string, object>
            {
                { "OrganizationId", newOrganizationId ?? "default" },
                { "LastActivity", DateTime.UtcNow }
            };

            return await _repository.UpdateImpersonationSessionAsync(impersonationSessionId, updates);
        }

        public async Task<string> CreateAndBackupImpersonationSessionAsync(
            string userId,
            string rootTenantId,
            string targetTenantId,
            string clientId,
            string? organizationId)
        {
            var sessionId = Guid.NewGuid().ToString();

            // Create impersonation session record
            var impersonationSession = new ImpersonationSession
            {
                Id = sessionId,
                UserId = userId,
                TargetTenantId = targetTenantId,
                RootTenantId = rootTenantId,
                ClientId = clientId,
                OrganizationId = organizationId ?? "default",
                StartedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                Status = "active"
            };
            var bc = BlocksContext.GetContext();

            var dbInsertSuccess = await _repository.InsertImpersonationSessionAsync(impersonationSession);
            if (!dbInsertSuccess)
            {
                throw new InvalidOperationException("Failed to create impersonation session in database");
            }

            return sessionId;
        }

    }
}
