using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.SignupLinks;
using Moq;

namespace XUnitTest.IamTests.SignupLinks;

public class SignupLinkSummaryServiceTests : IDisposable
{
    private readonly Mock<ISignupLinkRepository> _links = new();
    private readonly Mock<ISignupLinkConfigurationRepository> _configs = new();
    private readonly Mock<ISignupLinkRedemptionRepository> _redemptions = new();
    private readonly Mock<IValidator<SignupLinkSummaryRequest>> _validator = new();

    private const string TenantId = "tenant-a";
    private const string CfgId = "cfg1";

    public SignupLinkSummaryServiceTests()
    {
        BlocksContext.IsTestMode = true;
        InstallContext(TenantId);
        _validator.Setup(v => v.ValidateAsync(It.IsAny<SignupLinkSummaryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
    }

    public void Dispose()
    {
        BlocksContext.SetContext(null!);
        BlocksContext.IsTestMode = false;
        GC.SuppressFinalize(this);
    }

    private static void InstallContext(string tenantId) =>
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId: tenantId, roles: ["clouduser"], userId: "u1", impersonated: false,
            isAuthenticated: true, requestUri: "https://test", organizationId: "default",
            permissions: ["blocks-iam::iam::manage-signup-links"], expireOn: DateTime.UtcNow.AddHours(1),
            email: "a@b.com", userName: "tester", phoneNumber: null, displayName: "T",
            oauthToken: null, originalTenantId: tenantId, impersonationSessionId: null,
            applicationDomain: "test"));

    private SignupLinkSummaryService Sut() =>
        new(_links.Object, _configs.Object, _redemptions.Object, _validator.Object);

    private static SignupLink Link(
        string id,
        int redemptionCount,
        SignupLinkStatus status,
        DateTime expiresAt,
        DateTime? created = null) => new()
    {
        ItemId = id,
        TenantId = TenantId,
        ConfigurationId = CfgId,
        RedemptionCount = redemptionCount,
        Status = status,
        ExpiresAtUtc = expiresAt,
        CreatedDate = created ?? DateTime.UtcNow.AddDays(-1),
        Email = "secret@example.com",
        CodeHash = "hash-must-not-leak",
        RedirectUri = "https://app.example/cb"
    };

    [Fact]
    public void Classify_H2_H3_H4_Invariants_AndOpenedButAbandoned()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var links = new List<SignupLink>
        {
            Link("used1", 1, SignupLinkStatus.Redeemed, now.AddDays(1)),
            Link("used2", 2, SignupLinkStatus.Redeemed, now.AddDays(1)),
            Link("rev1", 0, SignupLinkStatus.Revoked, now.AddDays(1)),
            Link("rev2", 0, SignupLinkStatus.Revoked, now.AddDays(-1)), // revoked wins over expired
            Link("exp1", 0, SignupLinkStatus.Active, now.AddHours(-1)), // H4 Active+past expiry
            Link("act1", 0, SignupLinkStatus.Active, now.AddDays(1)), // opened-but-abandoned style
            Link("act2", 0, SignupLinkStatus.Active, now.AddDays(2)),
        };

        var (used, neverUsed, breakdown) = SignupLinkSummaryService.Classify(links, now);

        used.Should().Be(2);
        neverUsed.Should().Be(5);
        breakdown.Revoked.Should().Be(2);
        breakdown.Expired.Should().Be(1);
        breakdown.Active.Should().Be(2);
        (used + neverUsed).Should().Be(links.Count);
        (breakdown.Active + breakdown.Expired + breakdown.Revoked).Should().Be(neverUsed);
    }

    [Fact]
    public async Task Summarize_H1_DefaultsLast30Days_EchoesBoundsAndName()
    {
        var config = new SignupLinkConfiguration
        {
            ItemId = CfgId,
            TenantId = TenantId,
            Name = "Partner onboarding",
            IsActive = true
        };
        _configs.Setup(c => c.GetByIdAsync(CfgId, TenantId)).ReturnsAsync(config);
        _links.Setup(l => l.FindForSummaryAsync(
                TenantId, CfgId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<SignupLink>());
        _redemptions.Setup(r => r.CountRejectedForLinksAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(0);

        var before = DateTime.UtcNow;
        var (response, errors) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest { ConfigurationId = CfgId });
        var after = DateTime.UtcNow;

        errors.Should().BeNull();
        response.Should().NotBeNull();
        response!.ConfigurationId.Should().Be(CfgId);
        response.ConfigurationName.Should().Be("Partner onboarding");
        response.ToUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after.AddSeconds(1));
        response.FromUtc.Should().BeCloseTo(response.ToUtc.AddDays(-30), TimeSpan.FromSeconds(2));
        response.TotalGenerated.Should().Be(0);
        response.Used.Should().Be(0);
        response.NeverUsed.Should().Be(0);
        response.NeverUsedBreakdown.Active.Should().Be(0);
        response.RejectedAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Summarize_H5_H7_CountsAndRejectedScopedToConfiguration()
    {
        var now = DateTime.UtcNow;
        var links = new List<SignupLink>
        {
            Link("u1", 1, SignupLinkStatus.Redeemed, now.AddDays(1)),
            Link("n1", 0, SignupLinkStatus.Active, now.AddDays(1)),
            Link("r1", 0, SignupLinkStatus.Revoked, now.AddDays(1)),
        };
        _configs.Setup(c => c.GetByIdAsync(CfgId, TenantId)).ReturnsAsync(new SignupLinkConfiguration
        {
            ItemId = CfgId, TenantId = TenantId, Name = "cfg1"
        });
        _links.Setup(l => l.FindForSummaryAsync(TenantId, CfgId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(links);
        _redemptions.Setup(r => r.CountRejectedForLinksAsync(
                TenantId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(2);

        var (response, errors) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest { ConfigurationId = CfgId });

        errors.Should().BeNull();
        response!.TotalGenerated.Should().Be(3);
        response.Used.Should().Be(1);
        response.NeverUsed.Should().Be(2);
        response.NeverUsedBreakdown.Active.Should().Be(1);
        response.NeverUsedBreakdown.Revoked.Should().Be(1);
        response.RejectedAttempts.Should().Be(2);
        (response.Used + response.NeverUsed).Should().Be(response.TotalGenerated);
    }

    [Fact]
    public async Task Summarize_H6_ExplicitRangeEchoedUnchanged()
    {
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
        _configs.Setup(c => c.GetByIdAsync(CfgId, TenantId)).ReturnsAsync(new SignupLinkConfiguration
        {
            ItemId = CfgId, TenantId = TenantId, Name = "cfg1"
        });
        _links.Setup(l => l.FindForSummaryAsync(TenantId, CfgId, from, to))
            .ReturnsAsync(new List<SignupLink>());

        var (response, errors) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest
        {
            ConfigurationId = CfgId,
            FromUtc = from,
            ToUtc = to
        });

        errors.Should().BeNull();
        response!.FromUtc.Should().Be(from);
        response.ToUtc.Should().Be(to);
        _links.Verify(l => l.FindForSummaryAsync(TenantId, CfgId, from, to), Times.Once);
    }

    [Fact]
    public async Task Summarize_C1_MissingConfigurationId_NoAggregation()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<SignupLinkSummaryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(new[]
            {
                new ValidationFailure("ConfigurationId", "ConfigurationId is required")
            }));

        var (response, errors) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest());

        response.Should().BeNull();
        errors.Should().ContainKey("ConfigurationId");
        _links.Verify(l => l.FindForSummaryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Summarize_C2_InvertedRange()
    {
        var (response, errors) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest
        {
            ConfigurationId = CfgId,
            FromUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
        });

        response.Should().BeNull();
        errors.Should().ContainKey("FromUtc");
        _links.Verify(l => l.FindForSummaryAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Summarize_C3_RangeExceeds366Days()
    {
        var to = DateTime.UtcNow.Date;
        var from = to.AddDays(-400);
        var (response, errors) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest
        {
            ConfigurationId = CfgId,
            FromUtc = from,
            ToUtc = to
        });

        response.Should().BeNull();
        errors.Should().ContainKey("Range");
    }

    [Fact]
    public async Task Summarize_C3_FutureToUtc()
    {
        var (response, errors) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest
        {
            ConfigurationId = CfgId,
            FromUtc = DateTime.UtcNow.AddDays(-1),
            ToUtc = DateTime.UtcNow.AddDays(2)
        });

        response.Should().BeNull();
        errors.Should().ContainKey("ToUtc");
    }

    [Fact]
    public async Task Summarize_C5_UnknownConfiguration_ZerosAndNullName()
    {
        _configs.Setup(c => c.GetByIdAsync("foreign", TenantId)).ReturnsAsync((SignupLinkConfiguration?)null);
        _links.Setup(l => l.FindForSummaryAsync(TenantId, "foreign", It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<SignupLink>());

        var (response, errors) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest
        {
            ConfigurationId = "foreign"
        });

        errors.Should().BeNull();
        response!.ConfigurationName.Should().BeNull();
        response.TotalGenerated.Should().Be(0);
        response.Used.Should().Be(0);
        response.NeverUsed.Should().Be(0);
        response.RejectedAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Summarize_C6_EmptyConfig_ZerosWithName()
    {
        _configs.Setup(c => c.GetByIdAsync(CfgId, TenantId)).ReturnsAsync(new SignupLinkConfiguration
        {
            ItemId = CfgId, TenantId = TenantId, Name = "Empty cfg"
        });
        _links.Setup(l => l.FindForSummaryAsync(TenantId, CfgId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<SignupLink>());

        var (response, errors) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest { ConfigurationId = CfgId });

        errors.Should().BeNull();
        response!.ConfigurationName.Should().Be("Empty cfg");
        response.TotalGenerated.Should().Be(0);
    }

    [Fact]
    public async Task Summarize_C7_ResponseShapeHasNoLinkSecrets()
    {
        _configs.Setup(c => c.GetByIdAsync(CfgId, TenantId)).ReturnsAsync(new SignupLinkConfiguration
        {
            ItemId = CfgId, TenantId = TenantId, Name = "cfg1"
        });
        _links.Setup(l => l.FindForSummaryAsync(TenantId, CfgId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<SignupLink>
            {
                Link("secret-link", 0, SignupLinkStatus.Active, DateTime.UtcNow.AddDays(1))
            });
        _redemptions.Setup(r => r.CountRejectedForLinksAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(0);

        var (response, _) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest { ConfigurationId = CfgId });

        var json = System.Text.Json.JsonSerializer.Serialize(response);
        json.Should().NotContain("secret-link");
        json.Should().NotContain("secret@example.com");
        json.Should().NotContain("hash-must-not-leak");
        json.Should().NotContain("https://app.example/cb");
        json.Should().NotContain("CodeHash");
        json.Should().NotContain("RedirectUri");
        json.Should().NotContain("\"Email\"");
    }

    [Fact]
    public async Task Summarize_C8_ArchivedConfiguration_StillResolvesName()
    {
        _configs.Setup(c => c.GetByIdAsync(CfgId, TenantId)).ReturnsAsync(new SignupLinkConfiguration
        {
            ItemId = CfgId, TenantId = TenantId, Name = "Archived Partner", IsActive = false
        });
        _links.Setup(l => l.FindForSummaryAsync(TenantId, CfgId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<SignupLink>
            {
                Link("l1", 0, SignupLinkStatus.Active, DateTime.UtcNow.AddDays(1))
            });
        _redemptions.Setup(r => r.CountRejectedForLinksAsync(
                It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(0);

        var (response, errors) = await Sut().SummarizeAsync(new SignupLinkSummaryRequest { ConfigurationId = CfgId });

        errors.Should().BeNull();
        response!.ConfigurationName.Should().Be("Archived Partner");
        response.TotalGenerated.Should().Be(1);
    }

    [Fact]
    public void Classify_C9_SingleNow_BoundaryStable()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var onBoundary = Link("b", 0, SignupLinkStatus.Active, now); // ExpiresAtUtc <= now → expired
        var (used, neverUsed, breakdown) = SignupLinkSummaryService.Classify([onBoundary], now);
        used.Should().Be(0);
        neverUsed.Should().Be(1);
        breakdown.Expired.Should().Be(1);
        breakdown.Active.Should().Be(0);
    }
}
