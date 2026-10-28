using Blocks.Genesis;
using Blocks.Genesis.Notifications;

namespace Iam.DomainService.Notifications
{
    /// <summary>One notification for the Notifier: the config to use, who gets it and the short payload.</summary>
    /// <param name="TenantId">The tenant where the action happened; it signs the call.</param>
    /// <param name="ConfigurationName">Name of the built-in notification config.</param>
    /// <param name="UserIds">The users the event is about. The notification config decides who else receives it.</param>
    /// <param name="Payload">Ids and short text only. Serialized as it is.</param>
    /// <param name="OrganizationId">
    /// The record owner's organization. Null: the organization of the current context. A same-tenant user-token
    /// send always uses the token's organization (Genesis rule).
    /// </param>
    /// <param name="IncludeImpersonatedActor">
    /// True (default): when a root user acts inside the project, that user is named too, so console users get it.
    /// False for notices about another person's account, where the actor must not get their own notice.
    /// </param>
    public sealed record BuiltInNotification(
        string? TenantId,
        string ConfigurationName,
        IReadOnlyList<string> UserIds,
        object Payload,
        string? OrganizationId = null,
        bool IncludeImpersonatedActor = true);

    /// <summary>Queues a built-in notification on the Genesis publisher. Best effort: never throws.</summary>
    public interface IBuiltInNotificationSender
    {
        /// <returns>True when the notification was handed to the publisher.</returns>
        Task<bool> SendAsync(BuiltInNotification notification);
    }

    public static class BuiltInNotificationSenderExtensions
    {
        /// <summary>
        /// Call sites use this: no sender wired (tests, old hosts) or any failure means nothing
        /// is sent and the action goes on.
        /// </summary>
        public static async Task TrySendAsync(this IBuiltInNotificationSender? sender, BuiltInNotification notification)
        {
            if (sender is null)
            {
                return;
            }

            try
            {
                await sender.SendAsync(notification);
            }
            catch
            {
                // The sender itself never throws; this guards replacements of it.
            }
        }

        /// <summary>
        /// Tells the inviter that the invitation mail for <paramref name="invitedUserId"/> could not be queued.
        /// Ids and a reason code only. No inviter (self signup, queue job without one) means nobody to tell.
        /// </summary>
        public static Task InvitationMailFailedAsync(
            this IBuiltInNotificationSender? sender,
            string? tenantId,
            string? inviterUserId,
            string? organizationId,
            string invitedUserId,
            string reason)
        {
            if (string.IsNullOrWhiteSpace(inviterUserId))
            {
                return Task.CompletedTask;
            }

            return sender.TrySendAsync(new BuiltInNotification(
                tenantId,
                BuiltInNotificationNames.InvitationMailFailed,
                BuiltInNotificationSender.Recipients(inviterUserId),
                BuiltInNotificationSender.Payload(
                    BuiltInNotificationNames.InvitationMailFailed,
                    "Invitation not sent",
                    "An invitation mail could not be sent.",
                    isSuccess: false,
                    fields: new Dictionary<string, object?>
                    {
                        ["invitedUserId"] = invitedUserId,
                        ["reason"] = reason
                    }),
                organizationId,
                IncludeImpersonatedActor: false));
        }
    }

    /// <summary>Short reason codes for <c>iam-invitation-mail-failed</c>. Never exception text or an address.</summary>
    public static class InvitationMailFailureReasons
    {
        public const string NoEmailAddress = "no_email_address";
        public const string LinkNotBuilt = "link_not_built";
        public const string NotQueued = "not_queued";
        public const string QueueError = "queue_error";
    }

    /// <inheritdoc />
    public sealed class BuiltInNotificationSender : IBuiltInNotificationSender
    {
        private readonly INotificationPublisher _publisher;

        public BuiltInNotificationSender(INotificationPublisher publisher)
        {
            _publisher = publisher;
        }

        /// <summary>The non-empty user ids, without duplicates.</summary>
        public static List<string> Recipients(params string?[] userIds) =>
            Recipients((IEnumerable<string?>)userIds);

        /// <summary>The non-empty user ids, without duplicates.</summary>
        public static List<string> Recipients(IEnumerable<string?>? userIds) =>
            (userIds ?? Enumerable.Empty<string?>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// The common payload shape: <c>event</c>, <c>title</c>, <c>description</c>, optional
        /// <c>isSuccess</c>, the given ids/short fields, and <c>occurredAt</c>.
        /// </summary>
        public static Dictionary<string, object?> Payload(
            string configurationName,
            string title,
            string description,
            bool? isSuccess = null,
            IDictionary<string, object?>? fields = null)
        {
            var payload = new Dictionary<string, object?>
            {
                ["event"] = configurationName,
                ["title"] = title,
                ["description"] = description
            };

            if (isSuccess.HasValue)
            {
                payload["isSuccess"] = isSuccess.Value;
            }

            if (fields != null)
            {
                foreach (var (key, value) in fields)
                {
                    if (value is not null)
                    {
                        payload[key] = value;
                    }
                }
            }

            payload["occurredAt"] = DateTime.UtcNow;
            return payload;
        }

        /// <summary>
        /// The named users of a send: the given users plus, when a root user acts inside a project (impersonated),
        /// the acting user, so console users get the notification too.
        /// </summary>
        public static List<string> NamedUsers(IEnumerable<string?>? userIds, BlocksContext? context)
        {
            var named = Recipients(userIds);
            if (context is { Impersonated: true } && !string.IsNullOrWhiteSpace(context.UserId)
                && !named.Contains(context.UserId, StringComparer.Ordinal))
            {
                named.Add(context.UserId);
            }

            return named;
        }

        public Task<bool> SendAsync(BuiltInNotification notification)
        {
            if (notification is null || string.IsNullOrWhiteSpace(notification.ConfigurationName))
            {
                return Task.FromResult(false);
            }

            var context = BlocksContext.GetContext();
            var organizationId = string.IsNullOrWhiteSpace(notification.OrganizationId)
                ? context?.OrganizationId
                : notification.OrganizationId;

            // Publish returns at once and never throws.
            _publisher.Publish(new NotificationRequest
            {
                Name = notification.ConfigurationName,
                TenantId = notification.TenantId,
                NamedUserIds = NamedUsers(notification.UserIds, notification.IncludeImpersonatedActor ? context : null),
                OrganizationId = organizationId,
                Payload = notification.Payload
            });
            return Task.FromResult(true);
        }
    }
}
