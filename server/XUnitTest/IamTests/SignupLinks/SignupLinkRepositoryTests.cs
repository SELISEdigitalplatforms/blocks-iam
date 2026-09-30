using FluentAssertions;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.SignupLinks;

public class SignupLinkRepositoryTests
{
    private readonly Mock<IIdentityAccessManagementRepository> _iam = new();
    private readonly Mock<IMongoCollection<SignupLink>> _col;

    public SignupLinkRepositoryTests()
    {
        _col = MongoMock.Collection<SignupLink>();
        MongoMock.SetupIndexes(_col);
        _iam.Setup(r => r.GetCollectionByName<SignupLink>(SignupLinkRepository.CollectionName))
            .Returns(_col.Object);
    }

    private SignupLinkRepository Sut() =>
        new(_iam.Object, NullLogger<SignupLinkRepository>.Instance);

    [Fact]
    public async Task EnsureIndexesAsync_CreatesIndexes()
    {
        await Sut().EnsureIndexesAsync();
        _col.Verify(c => c.Indexes, Times.AtLeastOnce);
    }

    [Fact]
    public async Task InsertAsync_WritesDocument()
    {
        var entity = new SignupLink { ItemId = "l1", TenantId = "t1", Email = "a@b.com", CodeHash = "h" };
        await Sut().InsertAsync(entity);
        _col.Verify(c => c.InsertOneAsync(entity, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_UsesCollection()
    {
        MongoMock.SetupFind(_col, [new SignupLink { ItemId = "l1", TenantId = "t1", Email = "a@b.com", CodeHash = "h" }]);
        var got = await Sut().GetByIdAsync("l1", "t1");
        got.Should().NotBeNull();
        got!.Email.Should().Be("a@b.com");
    }

    [Fact]
    public async Task QueryAsync_ReturnsItemsAndCount()
    {
        MongoMock.SetupFind(_col, [
            new SignupLink { ItemId = "1", TenantId = "t1", Email = "a@b.com", CodeHash = "h1" },
            new SignupLink { ItemId = "2", TenantId = "t1", Email = "b@b.com", CodeHash = "h2" }
        ]);
        MongoMock.SetupCount(_col, 2);
        var (items, total) = await Sut().QueryAsync("t1", new QuerySignupLinksRequest { Page = 0, PageSize = 20 }, null);
        total.Should().Be(2);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReplaceAsync_Acknowledged()
    {
        var entity = new SignupLink { ItemId = "l1", TenantId = "t1", Email = "a@b.com", CodeHash = "h" };
        var ok = await Sut().ReplaceAsync(entity);
        ok.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeActiveByConfigurationAsync_UpdatesActive()
    {
        var count = await Sut().RevokeActiveByConfigurationAsync("t1", "cfg1", "actor", DateTime.UtcNow, null);
        count.Should().Be(2);
    }

    [Fact]
    public async Task FindForSummaryAsync_ReturnsMatchingLinks()
    {
        MongoMock.SetupFind(_col, [
            new SignupLink { ItemId = "1", TenantId = "t1", ConfigurationId = "cfg1", Email = "a@b.com", CodeHash = "h1" }
        ]);
        var items = await Sut().FindForSummaryAsync(
            "t1", "cfg1", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
        items.Should().HaveCount(1);
        items[0].ItemId.Should().Be("1");
    }
}
