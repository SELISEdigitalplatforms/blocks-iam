using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Resources
{
    /// <summary>Source-generated log messages for <see cref="ResourceMutationService"/>.</summary>
    internal static partial class ResourceMutationLog
    {
        [LoggerMessage(EventId = 9201, Level = LogLevel.Warning,
            Message = "Role '{Slug}' in organization '{OrganizationId}' moved, but rebuilding the ancestor chains of its {Count} descendant role(s) was not acknowledged. They keep their old chains until the next move.")]
        public static partial void DescendantAncestorsNotUpdated(ILogger logger, string slug, string organizationId, int count);

        [LoggerMessage(EventId = 9202, Level = LogLevel.Information,
            Message = "Role update end -- role {ItemId} belongs to another organization than the token's")]
        public static partial void RoleUpdateOutsideTokenOrganization(ILogger logger, string itemId);

        [LoggerMessage(EventId = 9203, Level = LogLevel.Information,
            Message = "Role update end -- making the requested parent the parent of '{Slug}' would close a cycle")]
        public static partial void RoleParentWouldCloseCycle(ILogger logger, string slug);
    }
}
