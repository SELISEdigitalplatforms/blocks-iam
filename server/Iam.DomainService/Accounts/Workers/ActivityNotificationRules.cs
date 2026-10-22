using System.Globalization;
using Iam.DomainService.Notifications;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Blocks.Genesis.Entities.Iam;

namespace Iam.DomainService.Accounts
{
    /// <summary>
    /// Which stored activity events also notify the user, and with what. Runs in the activity
    /// worker, so none of it is on the request path of login, password, MFA or session calls.
    /// Event names are the values written by the Authentication and Account services
    /// (<c>LoginAuditEvents</c>, <c>SessionAuditEvents</c>, <c>AccountEvents</c>).
    /// </summary>
    public static class ActivityNotificationRules
    {
        private const string UserIdKey = "userId";

        public const string LoginSuccess = "login_success";
        public const string PriorFailedAttemptsKey = "priorFailedAttempts";

        public const string ResetPassword = "Reset_Password";
        public const string ChangePassword = "Change_Password";

        public const string MfaEnrollmentCompleted = "mfa_enrollment_completed";
        public const string MfaEnrollmentFailed = "mfa_enrollment_failed";
        public const string MfaMethodChanged = "mfa_method_changed";
        public const string MfaRemoved = "mfa_removed";

        public const string UserActivated = "USER_ACTIVATED";
        public const string UserRevokedSession = "user_revoked_session";

        /// <summary>True when <see cref="Build"/> needs the user's login history for this event.</summary>
        public static bool NeedsLoginHistory(UserActivity activity) =>
            IsSuccessfulLogin(activity) && PriorFailedAttempts(activity) == 0
            && (!string.IsNullOrWhiteSpace(activity.Context?.IpAddress) || !string.IsNullOrWhiteSpace(activity.Context?.UserAgent));

        /// <summary>
        /// The notification for a stored activity, or null when it notifies nobody.
        /// <paramref name="loginHistory"/> is the history read before the activity was stored
        /// (null when not read or not available).
        /// </summary>
        public static BuiltInNotification? Build(UserActivity activity, LoginHistory? loginHistory)
        {
            if (activity is null || string.IsNullOrWhiteSpace(activity.UserId))
            {
                return null;
            }

            var userId = activity.UserId;
            var tenantId = activity.TenantId;

            switch (activity.Event)
            {
                case ResetPassword:
                case ChangePassword:
                    {
                        var reset = activity.Event == ResetPassword;
                        return Notify(tenantId, userId, BuiltInNotificationNames.PasswordChanged,
                            "Password changed",
                            reset ? "Your password was reset." : "Your password was changed.",
                            null,
                            new Dictionary<string, object?> { [UserIdKey] = userId, ["change"] = reset ? "reset" : "changed" });
                    }

                case MfaEnrollmentCompleted:
                case MfaEnrollmentFailed:
                case MfaMethodChanged:
                case MfaRemoved:
                    {
                        var (change, description) = activity.Event switch
                        {
                            MfaEnrollmentCompleted => ("added", "A second sign-in factor was added to your account."),
                            MfaEnrollmentFailed => ("enrolment-failed", "Adding a second sign-in factor failed."),
                            MfaMethodChanged => ("method-changed", "Your second sign-in factor was changed."),
                            _ => ("removed", "A second sign-in factor was removed from your account.")
                        };
                        return Notify(tenantId, userId, BuiltInNotificationNames.MfaChanged,
                            "Sign-in factor changed",
                            description,
                            activity.Event != MfaEnrollmentFailed,
                            new Dictionary<string, object?> { [UserIdKey] = userId, ["change"] = change });
                    }

                case UserActivated:
                    return Notify(tenantId, userId, BuiltInNotificationNames.UserActivated,
                        "Account activated",
                        "An admin activated your account.",
                        null,
                        new Dictionary<string, object?> { [UserIdKey] = userId });

                case UserRevokedSession:
                    if (!string.Equals(activity.Outcome, IdpConstants.StatusSuccess, StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    return Notify(tenantId, userId, BuiltInNotificationNames.SessionRevoked,
                        "Session ended",
                        "One of your sessions was ended.",
                        null,
                        new Dictionary<string, object?>
                        {
                            [UserIdKey] = userId,
                            ["clientId"] = string.IsNullOrWhiteSpace(activity.ClientId) ? null : activity.ClientId,
                            ["reason"] = string.IsNullOrWhiteSpace(activity.ReasonCode) ? null : activity.ReasonCode
                        });

                case LoginSuccess:
                    return BuildNewLogin(activity, loginHistory);

                default:
                    return null;
            }
        }

        private static BuiltInNotification? BuildNewLogin(UserActivity activity, LoginHistory? loginHistory)
        {
            if (!IsSuccessfulLogin(activity))
            {
                return null;
            }

            string reason;
            string description;
            if (PriorFailedAttempts(activity) > 0)
            {
                reason = "after-failed-attempts";
                description = "Your account was signed in to after failed attempts.";
            }
            else if (loginHistory is { HasEarlierLogin: true, SeenThisDevice: false })
            {
                reason = "new-device";
                description = "Your account was signed in to from a new device.";
            }
            else
            {
                return null;
            }

            return Notify(activity.TenantId, activity.UserId!, BuiltInNotificationNames.NewLogin,
                "New sign-in",
                description,
                null,
                new Dictionary<string, object?>
                {
                    [UserIdKey] = activity.UserId,
                    ["reason"] = reason,
                    ["clientId"] = string.IsNullOrWhiteSpace(activity.ClientId) ? null : activity.ClientId
                });
        }

        private static bool IsSuccessfulLogin(UserActivity activity) =>
            string.Equals(activity.Event, LoginSuccess, StringComparison.Ordinal)
            && string.Equals(activity.Outcome, IdpConstants.StatusSuccess, StringComparison.OrdinalIgnoreCase);

        private static int PriorFailedAttempts(UserActivity activity) =>
            activity.Metadata != null
            && activity.Metadata.TryGetValue(PriorFailedAttemptsKey, out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            && count > 0
                ? count
                : 0;

        private static BuiltInNotification Notify(
            string? tenantId,
            string userId,
            string configurationName,
            string title,
            string description,
            bool? isSuccess,
            Dictionary<string, object?> fields) =>
            new(tenantId,
                configurationName,
                BuiltInNotificationSender.Recipients(userId),
                BuiltInNotificationSender.Payload(configurationName, title, description, isSuccess, fields));
    }
}
