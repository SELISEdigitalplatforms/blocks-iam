namespace Iam.DomainService.Enums
{
    /// <summary>
    /// Kind of mutation event the IAM change-stream emits. Every write to a
    /// permission, role, group, or user record produces exactly one event of
    /// this type, captured for audit and replicated to downstream consumers.
    /// </summary>
    public enum MutationEventType
    {
        /// <summary>Sentinel value used when no mutation has been recorded.</summary>
        None,

        /// <summary>A new record was inserted.</summary>
        Create,

        /// <summary>An existing record was modified.</summary>
        Update,

        /// <summary>An existing record was removed (hard delete) or tombstoned (soft delete).</summary>
        Delete
    }
}