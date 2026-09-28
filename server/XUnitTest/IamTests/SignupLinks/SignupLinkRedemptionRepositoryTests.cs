using FluentAssertions;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Moq;
using XUnitTest.TestSupport;

namespace XUnitTest.IamTests.SignupLinks;

public class SignupLinkRedemptionRepositoryTests
{
    private readonly Mock<IIdentityAccessManagementRepository> _iam = new();
    private readonly Mock<IMongoCollection<SignupLinkRedemption>> _col;

    public SignupLinkRedemptionRepositoryTests()
    {
        _col = MongoMock.Collection<SignupLinkRedemption>();
        MongoMock.SetupIndexes(_col);
        _iam.Setup(r => r.GetCollectionByName<SignupLinkRedemption>(SignupLinkRedemptionRepository.CollectionName))
            .Returns(_col.Object);
    }

    private SignupLinkRedemptionRepository Sut() =>
        new(_iam.Object, NullLogger<SignupLinkRedemptionRepository>.Instance);

    [Fact]
    public async Task EnsureIndexesAsync_CreatesIndexes()
    {
        await Sut().EnsureIndexesAsync();
        _col.Verify(c => c.Indexes, Times.AtLeastOnce);
    }

    [Fact]
    public async Task CountRejectedForLinksAsync_EmptyIds_ReturnsZero()
    {
        var count = await Sut().CountRejectedForLinksAsync(
            "t1", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, Array.Empty<string>());
        count.Should().Be(0);
        _col.Verify(c => c.CountDocumentsAsync(
            It.IsAny<FilterDefinition<SignupLinkRedemption>>(),
            It.IsAny<CountOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CountRejectedForLinksAsync_UsesCount()
    {
        MongoMock.SetupCount(_col, 3);
        var count = await Sut().CountRejectedForLinksAsync(
            "t1", DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, ["l1", "l2"]);
        count.Should().Be(3);
    }
}
