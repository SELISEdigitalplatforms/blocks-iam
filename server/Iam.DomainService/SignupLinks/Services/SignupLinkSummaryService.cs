using Blocks.Genesis;
using FluentValidation;

namespace Iam.DomainService.SignupLinks;

public class SignupLinkSummaryService : ISignupLinkSummaryService
{
    private const int DefaultWindowDays = 30;
    private const int MaxRangeDays = 366;
    private const string TenantIdKey = "TenantId";
    private const string TenantIdRequiredMessage = "TenantId is required";

    private readonly ISignupLinkRepository _links;
    private readonly ISignupLinkConfigurationRepository _configurations;
    private readonly ISignupLinkRedemptionRepository _redemptions;
    private readonly IValidator<SignupLinkSummaryRequest> _validator;

    public SignupLinkSummaryService(
        ISignupLinkRepository links,
        ISignupLinkConfigurationRepository configurations,
        ISignupLinkRedemptionRepository redemptions,
        IValidator<SignupLinkSummaryRequest> validator)
    {
        _links = links;
        _configurations = configurations;
        _redemptions = redemptions;
        _validator = validator;
    }

    public async Task<(SignupLinkSummaryResponse? Response, Dictionary<string, string>? Errors)> SummarizeAsync(
        SignupLinkSummaryRequest request)
    {
        var validation = await _validator.ValidateAsync(request);
        if (!validation.IsValid)
        {
            return (null, validation.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage));
        }

        var tenantId = BlocksContext.GetContext()?.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return (null, new Dictionary<string, string> { { TenantIdKey, TenantIdRequiredMessage } });
        }

        // C9: evaluate now exactly once for defaults and active/expired split.
        var now = DateTime.UtcNow;
        var toUtc = request.ToUtc ?? now;
        var fromUtc = request.FromUtc ?? toUtc.AddDays(-DefaultWindowDays);

        var rangeErrors = ValidateRange(fromUtc, toUtc, now);
        if (rangeErrors != null)
        {
            return (null, rangeErrors);
        }

        var configurationId = request.ConfigurationId.Trim();
        var config = await _configurations.GetByIdAsync(configurationId, tenantId);
        // C5/C8: unknown or foreign → zeros + null name; archived still resolves name.
        var configurationName = config?.Name;

        var baseLinks = await _links.FindForSummaryAsync(tenantId, configurationId, fromUtc, toUtc);
        var (used, neverUsed, breakdown) = Classify(baseLinks, now);

        long rejectedAttempts = 0;
        if (baseLinks.Count > 0)
        {
            rejectedAttempts = await _redemptions.CountRejectedForLinksAsync(
                tenantId,
                fromUtc,
                toUtc,
                baseLinks.Select(l => l.ItemId));
        }

        return (new SignupLinkSummaryResponse
        {
            ConfigurationId = configurationId,
            ConfigurationName = configurationName,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            TotalGenerated = baseLinks.Count,
            Used = used,
            NeverUsed = neverUsed,
            NeverUsedBreakdown = breakdown,
            RejectedAttempts = rejectedAttempts
        }, null);
    }

    private static Dictionary<string, string>? ValidateRange(DateTime fromUtc, DateTime toUtc, DateTime now)
    {
        if (fromUtc >= toUtc)
        {
            return new Dictionary<string, string>
            {
                { "FromUtc", "fromUtc must be earlier than toUtc" }
            };
        }

        if (toUtc > now)
        {
            return new Dictionary<string, string>
            {
                { "ToUtc", "toUtc must not be in the future" }
            };
        }

        if ((toUtc - fromUtc).TotalDays > MaxRangeDays)
        {
            return new Dictionary<string, string>
            {
                { "Range", "The range must not exceed 366 days" }
            };
        }

        return null;
    }

    /// <summary>
    /// Classifies links with a single frozen <paramref name="now"/> (C9).
    /// neverUsed order: revoked, then expired, then active (H3/H4).
    /// </summary>
    public static (long Used, long NeverUsed, NeverUsedBreakdown Breakdown) Classify(
        IReadOnlyList<SignupLink> links,
        DateTime now)
    {
        long used = 0;
        long revoked = 0;
        long expired = 0;
        long active = 0;

        foreach (var link in links)
        {
            if (link.RedemptionCount > 0)
            {
                used++;
                continue;
            }

            if (link.Status == SignupLinkStatus.Revoked)
            {
                revoked++;
            }
            else if (link.ExpiresAtUtc <= now)
            {
                expired++;
            }
            else
            {
                active++;
            }
        }

        var neverUsed = revoked + expired + active;
        return (used, neverUsed, new NeverUsedBreakdown
        {
            Active = active,
            Expired = expired,
            Revoked = revoked
        });
    }
}
