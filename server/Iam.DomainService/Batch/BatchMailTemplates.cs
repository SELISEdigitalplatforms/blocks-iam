using Blocks.Genesis;
using Iam.DomainService.Utilities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Iam.DomainService.Batch
{
    /// <summary>A mail template as the mail phase needs it: its name (the mail purpose), language and organization.</summary>
    /// <param name="OrganizationId">The owning organization; <c>default</c> for the Default organization's templates.</param>
    public sealed record BatchMailTemplate(string Name, string Language, string OrganizationId);

    public interface IBatchMailTemplateRepository
    {
        /// <summary>The tenant's templates whose name and language are among the given ones, in one read.</summary>
        Task<List<BatchMailTemplate>> GetAsync(string tenantId, IReadOnlyCollection<string> names, IReadOnlyCollection<string> languages);
    }

    /// <summary>
    /// Reads the tenant's <c>EmailTemplates</c>, the collection the mail service (blocks-os-net-react
    /// <c>MailRepository</c>) picks templates from. Only name, language and organization are read.
    /// </summary>
    public class BatchMailTemplateRepository : IBatchMailTemplateRepository
    {
        public const string CollectionName = "EmailTemplates";

        private readonly IDbContextProvider _dbContextProvider;

        public BatchMailTemplateRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<List<BatchMailTemplate>> GetAsync(string tenantId, IReadOnlyCollection<string> names, IReadOnlyCollection<string> languages)
        {
            ArgumentNullException.ThrowIfNull(names);
            ArgumentNullException.ThrowIfNull(languages);
            if (names.Count == 0 || languages.Count == 0)
            {
                return [];
            }

            var builder = Builders<BsonDocument>.Filter;
            var filter = builder.In("Name", names) & builder.In("Language", languages);
            var projection = Builders<BsonDocument>.Projection.Include("Name").Include("Language").Include("OrganizationId");
            var documents = await _dbContextProvider.GetCollection<BsonDocument>(tenantId, CollectionName)
                .Find(filter)
                .Project(projection)
                .ToListAsync();

            return documents
                .Select(d => new BatchMailTemplate(
                    StringOf(d, "Name"),
                    StringOf(d, "Language"),
                    BatchMailTemplates.NormalizeOrganization(StringOf(d, "OrganizationId"))))
                .ToList();
        }

        private static string StringOf(BsonDocument document, string field) =>
            document.TryGetValue(field, out var value) && value.IsString ? value.AsString : string.Empty;
    }

    /// <summary>
    /// The mail service's template choice, applied before a mail is queued so a mail without a template is
    /// skipped instead of failing silently in the mail service: same name and same language (exact), the
    /// organization's own template first, then the Default organization's.
    /// </summary>
    public static class BatchMailTemplates
    {
        /// <summary>An empty organization is the Default organization, as in the mail service.</summary>
        public static string NormalizeOrganization(string? organizationId) =>
            string.IsNullOrWhiteSpace(organizationId) ? IdpConstants.DefaultOrganizationId : organizationId;

        /// <summary>The organization whose template is used, or null when neither it nor the Default organization has one.</summary>
        public static string? Choose(IReadOnlyCollection<BatchMailTemplate> templates, string name, string language, string organizationId)
        {
            ArgumentNullException.ThrowIfNull(templates);
            var organization = NormalizeOrganization(organizationId);
            var matching = templates
                .Where(t => string.Equals(t.Name, name, StringComparison.Ordinal) && string.Equals(t.Language, language, StringComparison.Ordinal))
                .ToList();

            if (matching.Any(t => t.OrganizationId == organization))
            {
                return organization;
            }

            return matching.Any(t => t.OrganizationId == IdpConstants.DefaultOrganizationId)
                ? IdpConstants.DefaultOrganizationId
                : null;
        }
    }
}
