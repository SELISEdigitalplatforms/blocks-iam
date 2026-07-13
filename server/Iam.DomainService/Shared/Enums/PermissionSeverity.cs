namespace Iam.DomainService.Enums
{
    /// <summary>
    /// Severity assigned to a permission. The platform uses severity to drive
    /// approval workflows (high-severity grants require an additional
    /// approver), UI emphasis, and the priority of audit alerts.
    /// </summary>
    public enum PermissionSeverity
    {
        /// <summary>Severity has not been set; treated as the lowest tier.</summary>
        None,

        /// <summary>Privileged action that can compromise the tenant (e.g. delete organisation, rotate root key).</summary>
        Critical,

        /// <summary>Sensitive action that exposes customer data or grants broad read access.</summary>
        High,

        /// <summary>Routine administrative action with limited blast radius.</summary>
        Medium,

        /// <summary>Cosmetic or read-only action with no security impact.</summary>
        Low
    }
}