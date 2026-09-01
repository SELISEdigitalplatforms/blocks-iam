using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Authentication.DomainService.Migrations
{
    /// <summary>
    /// Idempotently moves retired per-client branding into the tenant-level OIDC UI template.
    /// </summary>
    public sealed class OidcUiTemplateMigrationService
    {
        private static readonly Regex HexColorRegex = new(
            "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        private readonly ITenants _tenants;
        private readonly IAuthenticationRepository _repository;
        private readonly ILegacyOidcClientBrandingReader _legacyReader;
        private readonly ILogger<OidcUiTemplateMigrationService> _logger;

        public OidcUiTemplateMigrationService(
            ITenants tenants,
            IAuthenticationRepository repository,
            ILegacyOidcClientBrandingReader legacyReader,
            ILogger<OidcUiTemplateMigrationService> logger)
        {
            _tenants = tenants;
            _repository = repository;
            _legacyReader = legacyReader;
            _logger = logger;
        }

        public async Task<OidcUiTemplateMigrationSummary> RunAsync(CancellationToken cancellationToken = default)
        {
            var summary = new OidcUiTemplateMigrationSummary();
            var originalContext = BlocksContext.GetContext();
            var tenants = _tenants.GetTenantDatabaseConnectionStrings();

            foreach (var tenant in tenants.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                summary.TenantsExamined++;

                try
                {
                    SetMigrationContext(tenant.Key);

                    var existingTemplate = await _repository.GetOidcUiTemplateAsync();
                    if (existingTemplate is not null)
                    {
                        if (existingTemplate.SchemaVersion >= OidcUiTemplate.CurrentSchemaVersion)
                        {
                            summary.TenantsSkipped++;
                            continue;
                        }

                        var upgradedTemplate = IdpService.MergeOidcUiTemplateWithDefaults(existingTemplate);
                        upgradedTemplate.ItemId ??= Guid.NewGuid().ToString();
                        await _repository.SaveOidcUiTemplateAsync(upgradedTemplate);
                        summary.TenantsMigrated++;
                        continue;
                    }

                    var clients = await _legacyReader.ReadAsync(
                        tenant.Value.Item1,
                        tenant.Value.Item2,
                        cancellationToken);
                    var source = SelectSourceClient(clients);
                    if (source is null)
                    {
                        summary.TenantsSkipped++;
                        continue;
                    }

                    var template = IdpService.CreateDefaultOidcUiTemplate();
                    template.ItemId = Guid.NewGuid().ToString();

                    if (source.LegacyLogoUrl is not null)
                    {
                        template.Branding!.LogoUrl = source.LegacyLogoUrl;
                    }

                    if (source.LegacyBrandColor is not null && HexColorRegex.IsMatch(source.LegacyBrandColor))
                    {
                        template.Theme!.Light!.Primary = source.LegacyBrandColor;
                        template.Theme.Dark!.Primary = source.LegacyBrandColor;
                    }

                    await _repository.SaveOidcUiTemplateAsync(template);
                    summary.TenantsMigrated++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    summary.TenantsFailed++;
                    _logger.LogError(
                        exception,
                        "Failed to migrate legacy OIDC UI branding for tenant {TenantId}.",
                        tenant.Key);
                }
                finally
                {
                    BlocksContext.SetContext(originalContext);
                }
            }

            _logger.LogInformation(
                "OIDC UI template migration completed. Examined={Examined} Migrated={Migrated} Skipped={Skipped} Failed={Failed}",
                summary.TenantsExamined,
                summary.TenantsMigrated,
                summary.TenantsSkipped,
                summary.TenantsFailed);

            return summary;
        }

        public static LegacyOidcClientBranding? SelectSourceClient(
            IEnumerable<LegacyOidcClientBranding> clients)
        {
            return clients
                .Where(client => client.LegacyLogoUrl is not null || client.LegacyBrandColor is not null)
                .OrderByDescending(client => client.IsActive)
                .ThenBy(client => client.ClientId, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static void SetMigrationContext(string tenantId)
        {
            BlocksContext.SetContext(BlocksContext.Create(
                tenantId: tenantId,
                roles: null,
                userId: "oidc-ui-template-migration",
                impersonated: false,
                isAuthenticated: true,
                requestUri: string.Empty,
                organizationId: "default",
                permissions: null,
                expireOn: DateTime.UtcNow.AddHours(1),
                email: string.Empty,
                userName: "oidc-ui-template-migration",
                phoneNumber: null,
                displayName: "OIDC UI Template Migration",
                oauthToken: null,
                originalTenantId: tenantId,
                impersonationSessionId: null,
                applicationDomain: string.Empty));
        }
    }
}
