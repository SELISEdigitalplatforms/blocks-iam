using Iam.DomainService.Shared.Entities;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Iam.DomainService.Utilities
{
    /// <summary>
    /// The one place that decides whether multi-organization mode is on for a tenant.
    /// </summary>
    /// <remarks>
    /// Extracted so the query side can ask the same question as the mutation side without a second
    /// copy of the null handling. The configuration is fetched with FirstOrDefaultAsync, so it is
    /// null for any tenant that never saved one -- a freshly provisioned tenant, or one seeded
    /// without it -- and dereferencing it directly is the bug this exists to prevent. Absent
    /// configuration means single-organization, which is also the correct answer: a tenant with no
    /// configuration has not enabled multi-org.
    /// </remarks>
    public static class MultiOrgMode
    {
        public static bool IsEnabled(TenantConfiguration? tenantConfig, ILogger logger, [CallerMemberName] string operation = "")
        {
            if (tenantConfig is not null)
            {
                return tenantConfig.IsMultiOrgEnabled;
            }

            logger?.LogWarning(
                "{Operation}: no tenant configuration document exists for this tenant, so multi-organization mode is treated as disabled and cross-organization propagation is skipped. Save the organization configuration to enable it.",
                operation);

            return false;
        }
    }
}
