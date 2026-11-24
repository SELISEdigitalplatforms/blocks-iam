using Iam.DomainService.Shared.Serialization;
using Iam.DomainService.Users;

namespace Iam.DomainService.Batch
{
    /// <summary>The outcome of the API-stage checks of one batch request.</summary>
    /// <param name="RequestErrors">Request-level errors (code -> message); when set, nothing is saved.</param>
    /// <param name="Accepted">Items that passed the basic checks, normalised for storage.</param>
    /// <param name="Rejected">Items that failed a basic check, in request order.</param>
    public sealed record BatchValidation(
        IDictionary<string, string>? RequestErrors,
        List<BatchJobItem> Accepted,
        List<BatchItemFailure> Rejected);

    /// <summary>
    /// The API-stage checks of POST iam/batch/users: pure, no reads. Request-level problems refuse the
    /// whole request; an item that fails a basic check is rejected alone and reported on the job.
    /// </summary>
    public static class BatchRequestValidator
    {
        /// <summary>The field of an item's email in failures.</summary>
        public const string EmailField = "email";

        public static BatchValidation Validate(BatchUsersRequest? request)
        {
            var requestErrors = ValidateRequest(request);
            if (requestErrors.Count > 0)
            {
                return new BatchValidation(requestErrors, [], []);
            }

            var accepted = new List<BatchJobItem>();
            var rejected = new List<BatchItemFailure>();
            var items = request!.Items!;
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var errors = ValidateItem(item);
                if (errors.Count > 0)
                {
                    rejected.Add(new BatchItemFailure
                    {
                        Ref = RefOrNull(item?.Ref),
                        Index = index,
                        Email = NormalizedEmailOrNull(item?.Email),
                        Errors = errors
                    });
                    continue;
                }

                accepted.Add(new BatchJobItem
                {
                    Index = index,
                    Ref = RefOrNull(item!.Ref),
                    Data = Normalized(item)
                });
            }

            return new BatchValidation(null, accepted, rejected);
        }

        private static Dictionary<string, string> ValidateRequest(BatchUsersRequest? request)
        {
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            var items = request?.Items;
            if (items is null || items.Count == 0)
            {
                errors[BatchErrorCodes.ItemsRequired] = "A batch needs at least one item";
                return errors;
            }

            if (items.Count > BatchLimits.MaxItems)
            {
                errors[BatchErrorCodes.ItemsLimitExceeded] = $"A batch can contain at most {BatchLimits.MaxItems} items";
                return errors;
            }

            var duplicateRef = items
                .Select(i => i?.Ref)
                .Where(r => !string.IsNullOrEmpty(r))
                .GroupBy(r => r!, StringComparer.Ordinal)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateRef is not null)
            {
                errors[BatchErrorCodes.DuplicateRef] = $"Duplicate ref in request: {duplicateRef.Key}";
            }

            var duplicateEmail = items
                .Select((item, index) => (Email: UserIdentityRules.NormalizeEmail(item?.Email), Index: index))
                .Where(e => e.Email.Length > 0)
                .GroupBy(e => e.Email, StringComparer.Ordinal)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicateEmail is not null)
            {
                errors[BatchErrorCodes.DuplicateEmail] =
                    $"Duplicate email in request: {duplicateEmail.Key} (items {string.Join(", ", duplicateEmail.Select(e => e.Index))})";
            }

            if (request!.ClientReference is { Length: > BatchLimits.MaxClientReferenceLength })
            {
                errors[BatchErrorCodes.ClientReferenceTooLong] =
                    $"clientReference can have at most {BatchLimits.MaxClientReferenceLength} characters";
            }

            return errors;
        }

        /// <summary>The basic checks of one item; the first failure per field.</summary>
        private static List<BatchItemError> ValidateItem(BatchUserItem? item)
        {
            var errors = new List<BatchItemError>();

            if (item?.Ref is { Length: > BatchLimits.MaxRefLength })
            {
                errors.Add(BatchErrorCodes.Error("ref", BatchErrorCodes.RefTooLong));
            }

            AddEmailError(errors, item?.Email);
            if (item is null)
            {
                return errors;
            }

            if (item.FirstName is { Length: > BatchLimits.MaxNameLength })
            {
                errors.Add(BatchErrorCodes.Error("firstName", BatchErrorCodes.FirstNameTooLong));
            }

            if (item.LastName is { Length: > BatchLimits.MaxNameLength })
            {
                errors.Add(BatchErrorCodes.Error("lastName", BatchErrorCodes.LastNameTooLong));
            }

            if (!string.IsNullOrWhiteSpace(item.PhoneNumber)
                && (!item.PhoneNumber.StartsWith('+') || item.PhoneNumber.Length > BatchLimits.MaxPhoneLength))
            {
                errors.Add(BatchErrorCodes.Error("phoneNumber", BatchErrorCodes.PhoneInvalid));
            }

            AddMembershipErrors(errors, item.Memberships);
            AddMailErrors(errors, item.Mail);
            return errors;
        }

        /// <summary>
        /// Only lengths: whether the template exists, and whether the user is new, is decided by the mail phase,
        /// which skips the mail and never fails the user for it.
        /// </summary>
        private static void AddMailErrors(List<BatchItemError> errors, BatchItemMail? mail)
        {
            if (mail is null)
            {
                return;
            }

            if (mail.Template is { Length: > BatchLimits.MaxMailTemplateLength })
            {
                errors.Add(BatchErrorCodes.Error("mail.template", BatchErrorCodes.MailTemplateTooLong));
            }

            if (mail.ExistingUserTemplate is { Length: > BatchLimits.MaxMailTemplateLength })
            {
                errors.Add(BatchErrorCodes.Error("mail.existingUserTemplate", BatchErrorCodes.MailTemplateTooLong));
            }

            if (mail.Language is { Length: > BatchLimits.MaxMailLanguageLength })
            {
                errors.Add(BatchErrorCodes.Error("mail.language", BatchErrorCodes.MailLanguageTooLong));
            }
        }

        private static void AddEmailError(List<BatchItemError> errors, string? email)
        {
            var normalized = UserIdentityRules.NormalizeEmail(email);
            if (normalized.Length == 0)
            {
                errors.Add(BatchErrorCodes.Error(EmailField, BatchErrorCodes.EmailRequired));
            }
            else if (normalized.Length > BatchLimits.MaxEmailLength)
            {
                errors.Add(BatchErrorCodes.Error(EmailField, BatchErrorCodes.EmailTooLong));
            }
            else if (!UserIdentityRules.IsValidEmail(normalized))
            {
                errors.Add(BatchErrorCodes.Error(EmailField, BatchErrorCodes.EmailInvalid));
            }
        }

        private static void AddMembershipErrors(List<BatchItemError> errors, List<BatchMembership>? memberships)
        {
            if (memberships is null)
            {
                return;
            }

            if (memberships.Count > BatchLimits.MaxMemberships)
            {
                errors.Add(BatchErrorCodes.Error("memberships", BatchErrorCodes.MembershipsLimitExceeded));
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < memberships.Count; i++)
            {
                var membership = memberships[i];
                if (!seen.Add(membership?.OrganizationId ?? string.Empty))
                {
                    errors.Add(BatchErrorCodes.Error($"memberships[{i}].organizationId", BatchErrorCodes.OrganizationDuplicate));
                }

                if (membership?.Permissions is { Count: > UserIdentityRules.MaxPermissionsPerUser })
                {
                    errors.Add(BatchErrorCodes.Error($"memberships[{i}].permissions", BatchErrorCodes.TooManyPermissions));
                }
            }
        }

        /// <summary>The item as the job stores it: normalised email, attributes as plain values, no unbound fields.</summary>
        private static BatchUserData Normalized(BatchUserItem item) => new()
        {
            Email = UserIdentityRules.NormalizeEmail(item.Email),
            Salutation = item.Salutation,
            FirstName = item.FirstName,
            LastName = item.LastName,
            PhoneNumber = item.PhoneNumber,
            Language = item.Language,
            Attributes = item.Attributes is null ? null : AttributeNormalizer.Normalize(item.Attributes, AttributePolicy.Internal),
            // A null entry stays in place as an empty membership, so indexes in error fields keep matching the request.
            Memberships = item.Memberships?
                .Select(m => new BatchMembership
                {
                    OrganizationId = m?.OrganizationId,
                    Roles = m?.Roles?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList(),
                    Permissions = m?.Permissions?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
                })
                .ToList(),
            Mail = item.Mail is { Send: true } mail
                ? new BatchItemMail
                {
                    Send = true,
                    Template = BlankToNull(mail.Template),
                    ExistingUserTemplate = BlankToNull(mail.ExistingUserTemplate),
                    Language = BlankToNull(mail.Language)
                }
                : null
        };

        private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? RefOrNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private static string? NormalizedEmailOrNull(string? email)
        {
            var normalized = UserIdentityRules.NormalizeEmail(email);
            return normalized.Length == 0 ? null : normalized;
        }
    }
}
