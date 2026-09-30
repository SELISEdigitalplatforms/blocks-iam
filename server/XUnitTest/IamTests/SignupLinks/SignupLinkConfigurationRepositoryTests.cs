using FluentAssertions;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.SignupLinks;

public class SignupLinkConfigurationRepositoryTests
{
    private readonly Mock<IIdentityAccessManagementRepository> _iam = new();
    private readonly Mock<IMongoCollection<SignupLinkConfiguration>> _col;

    public SignupLinkConfigurationRepositoryTests()
    {
        _col = MongoMock.Collection<SignupLinkConfiguration>();
        MongoMock.SetupIndexes(_col);
        _iam.Setup(r => r.GetCollectionByName<SignupLinkConfiguration>(SignupLinkConfigurationRepository.CollectionName))
            .Returns(_col.Object);
    }

    private SignupLinkConfigurationRepository Sut() =>
        new(_iam.Object, NullLogger<SignupLinkConfigurationRepository>.Instance);

    [Fact]
    public async Task EnsureIndexesAsync_CreatesIndexes()
    {
        await Sut().EnsureIndexesAsync();
        _col.Verify(c => c.Indexes, Times.AtLeastOnce);
    }

    [Fact]
    public async Task InsertAsync_WritesDocument()
    {
        var entity = new SignupLinkConfiguration { ItemId = "id-1", TenantId = "t1", Name = "A" };
        await Sut().InsertAsync(entity);
        _col.Verify(c => c.InsertOneAsync(entity, It.IsAny<InsertOneOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_UsesCollection()
    {
        MongoMock.SetupFind(_col, [new SignupLinkConfiguration { ItemId = "id-1", TenantId = "t1", Name = "A" }]);
        var got = await Sut().GetByIdAsync("id-1", "t1");
        got.Should().NotBeNull();
        got!.Name.Should().Be("A");
    }

    [Fact]
    public async Task QueryAsync_ReturnsItemsAndCount()
    {
        MongoMock.SetupFind(_col, [
            new SignupLinkConfiguration { ItemId = "1", TenantId = "t1", Name = "A", IsActive = true },
            new SignupLinkConfiguration { ItemId = "2", TenantId = "t1", Name = "B", IsActive = true }
        ]);
        MongoMock.SetupCount(_col, 2);
        // Query uses Find().Sort().Skip().Limit() fluent API — may need extra setups.
        // Wire Find sync path via FindAsync already in SetupFind.
        var (items, total) = await Sut().QueryAsync("t1", new QuerySignupLinkConfigurationsRequest { Page = 0, PageSize = 20 });
        total.Should().Be(2);
        items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReplaceAsync_Acknowledged()
    {
        var entity = new SignupLinkConfiguration { ItemId = "id-1", TenantId = "t1", Name = "A" };
        var ok = await Sut().ReplaceAsync(entity);
        ok.Should().BeTrue();
    }

    [Fact]
    public async Task FindByNameAsync_ReturnsMatch()
    {
        MongoMock.SetupFind(_col, [new SignupLinkConfiguration { ItemId = "id-1", TenantId = "t1", Name = "A" }]);
        var got = await Sut().FindByNameAsync("t1", "A");
        got.Should().NotBeNull();
    }
}
