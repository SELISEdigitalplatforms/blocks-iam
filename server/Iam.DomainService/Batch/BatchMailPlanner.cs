using Blocks.Genesis.Entities.Iam;
using Iam.DomainService.Utilities;

namespace Iam.DomainService.Batch
{
    /// <summary>The mail phase a finished import owes: its counts and skips so far, and the mails to send.</summary>
    public sealed record BatchMailPlan(BatchJobMail Mail, List<BatchMailEntry> Entries);

    /// <summary>
    /// Decides, after the users are written, which items get a mail and with which template. Pure: the template
    /// check and the sending are the mail phase's (<see cref="BatchMailService"/>).
    /// <list type="bullet">
    /// <item>Only items with <c>mail.send</c> are counted (<see cref="BatchJobMail.Requested"/>).</item>
    /// <item>A user this job created gets <c>mail.template</c> for its first membership's organization.</item>
    /// <item>A user that already existed gets <c>mail.existingUserTemplate</c> for the first organization this job
    /// added it to; without that template it is skipped (<c>user_exists</c>), and without a new organization
    /// (<c>no_new_membership</c>), so sending the same file again mails nobody twice.</item>
    /// <item>A user whose import failed gets no mail (<c>import_failed</c>).</item>
    /// </list>
    /// </summary>
    public static class BatchMailPlanner
    {
        /// <summary>Codes decided while planning; a resumed mail phase keeps these and decides the others again.</summary>
        public static readonly IReadOnlySet<string> PlanningCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            BatchErrorCodes.UserExists, BatchErrorCodes.NoNewMembership, BatchErrorCodes.TemplateRequired, BatchErrorCodes.ImportFailed
        };

        /// <param name="plans">The job's planned items, in request order.</param>
        /// <param name="writtenUserIds">The users the bulk write wrote.</param>
        /// <param name="organizations">The job's preloaded organizations, for the organization name.</param>
        /// <returns>Null when no item asked for a mail.</returns>
        public static BatchMailPlan? Build(
            IReadOnlyList<BatchItemPlan> plans,
            IReadOnlySet<string> writtenUserIds,
            IReadOnlyDictionary<string, Organization> organizations)
        {
            ArgumentNullException.ThrowIfNull(plans);
            ArgumentNullException.ThrowIfNull(writtenUserIds);
            ArgumentNullException.ThrowIfNull(organizations);

            var requested = plans.Where(p => p.Item.Data.Mail is { Send: true }).ToList();
            if (requested.Count == 0)
            {
                return null;
            }

            var mail = new BatchJobMail { Requested = requested.Count };
            var entries = new List<BatchMailEntry>();
            foreach (var plan in requested)
            {
                var skip = SkipCode(plan, writtenUserIds, out var template);
                if (skip is not null)
                {
                    mail.Results.Add(Skipped(plan, skip, template));
                    continue;
                }

                entries.Add(Entry(plan, template!, organizations));
            }

            mail.Skipped = mail.Results.Count;
            mail.Status = entries.Count > 0 ? BatchJobStatus.Queued : BatchJobStatus.Completed;
            mail.CompletedAt = entries.Count > 0 ? null : DateTime.UtcNow;
            return new BatchMailPlan(mail, entries);
        }

        private static string? SkipCode(BatchItemPlan plan, IReadOnlySet<string> writtenUserIds, out string? template)
        {
            var mail = plan.Item.Data.Mail!;
            template = null;
            switch (plan.Outcome)
            {
                case BatchItemOutcome.Created when plan.User is not null && writtenUserIds.Contains(plan.User.ItemId):
                    template = mail.Template;
                    return template is null ? BatchErrorCodes.TemplateRequired : null;

                case BatchItemOutcome.Updated when plan.User is not null && writtenUserIds.Contains(plan.User.ItemId):
                case BatchItemOutcome.Unchanged:
                    template = mail.ExistingUserTemplate;
                    if (template is null)
                    {
                        return BatchErrorCodes.UserExists;
                    }

                    return plan.MailOrganizationId is null ? BatchErrorCodes.NoNewMembership : null;

                default:
                    return BatchErrorCodes.ImportFailed;
            }
        }

        private static BatchMailResult Skipped(BatchItemPlan plan, string code, string? template) => new()
        {
            Ref = plan.Item.Ref,
            Index = plan.Item.Index,
            Email = plan.Item.Data.Email,
            Outcome = BatchMailOutcomes.Skipped,
            Code = code,
            Message = BatchErrorCodes.MessageFor(code),
            Template = template,
            Language = template is null ? null : LanguageOf(plan)
        };

        private static BatchMailEntry Entry(BatchItemPlan plan, string template, IReadOnlyDictionary<string, Organization> organizations)
        {
            var data = plan.Item.Data;
            var user = plan.User!;
            var organizationId = plan.MailOrganizationId!;
            return new BatchMailEntry
            {
                Index = plan.Item.Index,
                Ref = plan.Item.Ref,
                Email = data.Email ?? user.Email,
                UserId = user.ItemId,
                OrganizationId = organizationId,
                Template = template,
                Language = LanguageOf(plan),
                FirstName = data.FirstName ?? user.FirstName,
                LastName = data.LastName ?? user.LastName,
                OrganizationName = organizationId == IdpConstants.DefaultOrganizationId
                    ? "Default"
                    : organizations.TryGetValue(organizationId, out var organization) ? organization.Name : null
            };
        }

        private static string LanguageOf(BatchItemPlan plan) => plan.Item.Data.Mail?.Language ?? BatchLimits.DefaultMailLanguage;
    }
}
