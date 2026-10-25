using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blocks.Genesis;
using Blocks.Genesis.DataAccess;
using Blocks.Genesis.DataAccess.Rules;
using Blocks.Genesis.Entities.Iam;
using FluentAssertions;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace XUnitTest.IamTests.DataAccess;

/// <summary>
/// Part B for IAM: the wrapper runs a schema's validation rules on writes and its defaults on inserts, only
/// for schemas the Genesis allow-list opens, only with a signed-in user of the same tenant.
/// </summary>
public class IamWriteRulesTests : IDisposable
{
    private readonly Mock<IDataAccessResolver> _resolver = new();
    private readonly IamDataAccess _sut;
    private BsonDocument? _sent;

    public IamWriteRulesTests()
    {
        BlocksContext.IsTestMode = true;
        SetContext(authenticated: true);
        _sut = new IamDataAccess(_resolver.Object, NullLogger<IamDataAccess>.Instance);
    }

    public void Dispose()
    {
        BlocksContext.SetContext(null);
        BlocksContext.IsTestMode = false;
    }

    private static void SetContext(bool authenticated) =>
        BlocksContext.SetContext(BlocksContext.Create(
            "t1", new[] { "role" }, "user-1", authenticated, "uri", "org",
            DateTime.UtcNow.AddHours(1), "e@x.com", new[] { "perm" }, "octo",
            "phone", "display", "oauth", "t1"));

    /// <summary>The resolver answers with <paramref name="change"/> applied to the document it was sent.</summary>
    private void Resolves(Action<BsonDocument>? change = null, DataValidationResult? validation = null) =>
        _resolver.Setup(r => r.ResolveWriteAsync(It.IsAny<string>(), It.IsAny<BsonDocument>(), It.IsAny<bool>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, BsonDocument document, bool _, BsonDocument? _, CancellationToken _) =>
            {
                _sent = document;
                change?.Invoke(document);
                return new ResolvedWrite(document, validation ?? new DataValidationResult(), DefaultsApplied: change is not null);
            });

    private static DataValidationResult Failed(string field, string message)
    {
        var result = new DataValidationResult();
        result.AddError(field, message, "MaxLength");
        return result;
    }

    [Fact]
    public async Task Insert_DefaultsLandInTheCallersOwnInstance()
    {
        Resolves(document => document["FirstName"] = "Guest");
        var user = new User { ItemId = "u1", Email = "a@b.c" };

        var returned = await _sut.PrepareWriteAsync(IamSchemas.User, user, isInsert: true, "test");

        returned.Should().BeSameAs(user);
        user.FirstName.Should().Be("Guest");
        user.Email.Should().Be("a@b.c");
        _resolver.Verify(r => r.ResolveWriteAsync("User", It.IsAny<BsonDocument>(), true, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Insert_AddsTheRequesterToReadUpdateAndDeleteUserIds_KeepingIdsAlreadyThere()
    {
        Resolves();
        var record = new Role { ItemId = "r1", Name = "Editors" };
        record.Read = new OperationAccess { UserIds = ["user-9"], Roles = ["admin"] };
        record.Update = new OperationAccess { UserIds = ["user-1"] };

        await _sut.PrepareWriteAsync(IamSchemas.Role, record, isInsert: true, "test");

        record.Read!.UserIds.Should().Equal("user-9", "user-1");
        record.Read.Roles.Should().Equal("admin");
        record.Update!.UserIds.Should().Equal("user-1");   // once
        record.Delete!.UserIds.Should().Equal("user-1");
        record.Create.Should().BeNull();
    }

    [Fact]
    public async Task Insert_OnASchemaWithoutRules_StillAddsTheRequester()
    {
        var record = new Role { ItemId = "r1", Name = "Editors" };

        await _sut.PrepareWriteAsync("RefreshTokenModel", record, isInsert: true, "test");

        record.Read!.UserIds.Should().Equal("user-1");
    }

    [Fact]
    public async Task AReplace_OrAnInsertWithoutASignedInUser_AddsNoRequester()
    {
        Resolves();
        var replaced = new Role { ItemId = "r1", Name = "Editors" };
        await _sut.PrepareWriteAsync(IamSchemas.Role, replaced, isInsert: false, "test");
        replaced.Read.Should().BeNull();

        SetContext(authenticated: false);
        var anonymous = new Role { ItemId = "r1", Name = "Editors" };
        await _sut.PrepareWriteAsync(IamSchemas.Role, anonymous, isInsert: true, "test");
        anonymous.Read.Should().BeNull();
    }

    [Fact]
    public async Task ABrokenRule_Throws_WithThePropertyAndMessage()
    {
        Resolves(validation: Failed("FirstName", "FirstName must be at most 5 characters"));

        var act = () => _sut.PrepareWriteAsync(IamSchemas.Role, new Role { ItemId = "r1", Name = "Administrators" }, isInsert: false, "test");

        (await act.Should().ThrowAsync<DataValidationException>()).Which.Result.Errors.Should().ContainSingle()
            .Which.FieldName.Should().Be("FirstName");
    }

    [Theory]
    [InlineData(IamSchemas.RefreshTokenModel)]
    [InlineData(IamSchemas.UserActivity)]
    [InlineData(IamSchemas.ClientCredential)]
    public async Task SchemasTheAllowListKeepsClosed_NeverReachTheResolver(string schema)
    {
        await _sut.PrepareWriteAsync(schema, new User(), isInsert: true, "test");
        await _sut.CheckUpdateAsync(schema, Builders<User>.Update.Set(u => u.FirstName, "x"), "test");

        _resolver.Verify(r => r.ResolveWriteAsync(It.IsAny<string>(), It.IsAny<BsonDocument>(), It.IsAny<bool>(), It.IsAny<BsonDocument?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithoutASignedInUser_NoRulesRun()
    {
        SetContext(authenticated: false);

        await _sut.PrepareWriteAsync(IamSchemas.User, new User(), isInsert: true, "test");

        _resolver.Verify(r => r.ResolveWriteAsync(It.IsAny<string>(), It.IsAny<BsonDocument>(), It.IsAny<bool>(), It.IsAny<BsonDocument?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task APartialUpdate_SendsOnlyItsSetFields_AsNestedProperties()
    {
        Resolves();
        var update = Builders<User>.Update
            .Set("Read.Roles", new BsonArray { "admin" })
            .Set(u => u.FirstName, "Ann")
            .Set("Items.0.Name", "skipped")
            .Inc(u => u.FailedLoginCount, 1);

        await _sut.CheckUpdateAsync(IamSchemas.User, update, "test");

        _sent.Should().BeEquivalentTo(new BsonDocument
        {
            { "Read", new BsonDocument("Roles", new BsonArray { "admin" }) },
            { "FirstName", "Ann" },
        });
        _resolver.Verify(r => r.ResolveWriteAsync("User", It.IsAny<BsonDocument>(), false, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnUpdateWithNothingSet_IsNotChecked()
    {
        await _sut.CheckUpdateAsync(IamSchemas.User, Builders<User>.Update.Inc(u => u.FailedLoginCount, 1), "test");

        _resolver.Verify(r => r.ResolveWriteAsync(It.IsAny<string>(), It.IsAny<BsonDocument>(), It.IsAny<bool>(), It.IsAny<BsonDocument?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AMissingSchema_IsAllowed()
    {
        _resolver.Setup(r => r.ResolveWriteAsync(It.IsAny<string>(), It.IsAny<BsonDocument>(), It.IsAny<bool>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("no schema"));
        var role = new Role { ItemId = "r1" };

        (await _sut.PrepareWriteAsync(IamSchemas.Role, role, isInsert: true, "test")).Should().BeSameAs(role);
    }

    [Fact]
    public async Task TheInsertHelper_WritesNothing_WhenARuleFails()
    {
        var access = new Mock<IIamDataAccess>();
        access.Setup(a => a.InsertAllowedAsync(IamSchemas.Role, "test", null)).ReturnsAsync(true);
        access.Setup(a => a.PrepareWriteAsync(IamSchemas.Role, It.IsAny<Role>(), true, "test", null))
            .ThrowsAsync(new DataValidationException(Failed("Name", "Name is required")));
        // Strict: any InsertOneAsync on it would throw a MockException instead of the validation error.
        var collection = new Mock<IMongoCollection<Role>>(MockBehavior.Strict);

        var act = () => access.Object.InsertOneAsync(collection.Object, IamSchemas.Role, new Role { ItemId = "r1" }, "test");

        await act.Should().ThrowAsync<DataValidationException>();
    }
}
