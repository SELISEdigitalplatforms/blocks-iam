using Authentication.DomainService.SignupLinks;
using FluentAssertions;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.SignupLinks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using System.Text.Json;
using XUnitTest.TestSupport;
using Blocks.Genesis.Entities.Iam;

namespace XUnitTest.IamTests.SignupLinks;

/// <summary>
/// SPEC1 3.2 / 4.2: a redemption is one <c>signup-link-redeem</c> activity event; the summary counts old rows
/// plus new events with no double count. The count tests run on the local test Mongo.
/// </summary>
public class SignupLinkRedemptionEventTests : IDisposable
{
    private static readonly DateTime From = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Inside = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

    private readonly RealMongo _mongo = new();

    public void Dispose() => _mongo.Dispose();

    private UserActivityRepository Repo() =>
        new(_mongo.ProviderFor(), NullLogger<UserActivityRepository>.Instance);

    private static SignupLink Link(string id = "link-1", string tenant = "t1") => new()
    {
        ItemId = id,
        TenantId = tenant,
        Email = "invitee@example.com",
        OrganizationId = "org-1",
        Roles = ["r1"],
        Permissions = ["p1"]
    };

    private static HttpRequest Request()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.UserAgent = "ua-test";
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
        return http.Request;
    }

    // ---------- event shape (H2, C14) ----------

    [Fact]
    public void Event_Success_HasTheSection42Fields_AndNoEmail()
    {
        var e = SignupLinkRedemptionOrchestrator.BuildRedemptionEvent(
            Link(), SignupLinkRedemptionOutcome.UserCreated, null, "user-9", 3, Request(), grant: true);

        e.Category.Should().Be(UserActivityCategory.Account);
        e.Event.Should().Be("signup-link-redeem");
        e.Source.Should().Be("signup-link");
        e.Entity.Should().Be("SignupLink");
        e.EntityId.Should().Be("link-1");
        e.UserId.Should().BeEmpty();
        e.ActorUserId.Should().BeEmpty();
        e.TenantId.Should().Be("t1");
        e.Outcome.Should().Be("UserCreated");
        e.ReasonCode.Should().BeNull();
        e.ItemId.Should().HaveLength(32).And.MatchRegex("^[0-9a-f]+$");
        e.MessageId.Should().Be("signup-link-redemption:" + e.ItemId);
        e.Context!.UserAgent.Should().Be("ua-test");
        e.Context.IpAddress.Should().NotBeNullOrEmpty();
        e.Metadata!.Keys.Should().BeEquivalentTo("UserId", "RedemptionOrdinal", "GrantedOrganizationId", "GrantedRoles", "GrantedPermissions");
        e.Metadata["UserId"].Should().Be("user-9");
        e.Metadata["RedemptionOrdinal"].Should().Be("3");
        e.Metadata["GrantedOrganizationId"].Should().Be("org-1");
        JsonSerializer.Deserialize<string[]>(e.Metadata["GrantedRoles"]).Should().Equal("r1");
        JsonSerializer.Deserialize<string[]>(e.Metadata["GrantedPermissions"]).Should().Equal("p1");
        e.CreatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        JsonSerializer.Serialize(e).Should().NotContain("invitee@example.com").And.NotContainEquivalentOf("\"Email\"");
    }

    [Fact]
    public void Event_Rejection_CarriesTheReason_AndNoGrants()
    {
        var e = SignupLinkRedemptionOrchestrator.BuildRedemptionEvent(
            Link(), SignupLinkRedemptionOutcome.Rejected, "expired", null, 1, Request(), grant: false);

        e.Outcome.Should().Be("Rejected");
        e.ReasonCode.Should().Be("expired");
        e.Metadata!["UserId"].Should().BeEmpty();
        e.Metadata["GrantedOrganizationId"].Should().BeEmpty();
        e.Metadata["GrantedRoles"].Should().Be("[]");
        e.Metadata["GrantedPermissions"].Should().Be("[]");
    }

    [Fact]
    public void Event_IdsAreUniquePerRedemption()
    {
        var a = SignupLinkRedemptionOrchestrator.BuildRedemptionEvent(Link(), SignupLinkRedemptionOutcome.UserCreated, null, "u", 1, Request(), true);
        var b = SignupLinkRedemptionOrchestrator.BuildRedemptionEvent(Link(), SignupLinkRedemptionOutcome.UserCreated, null, "u", 1, Request(), true);
        a.ItemId.Should().NotBe(b.ItemId);
        a.MessageId.Should().NotBe(b.MessageId);
    }

    // ---------- activity API exposure (H2, C14, Q1) ----------

    [RealMongoFact]
    public async Task Event_IsHiddenFromASignedInUsersList_ButListedWithoutAUserFilter_AndHasNoEmail()
    {
        var repo = Repo();
        var e = SignupLinkRedemptionOrchestrator.BuildRedemptionEvent(
            Link(), SignupLinkRedemptionOutcome.UserCreated, null, "user-9", 1, Request(), grant: true);
        await repo.InsertAsync(e, CancellationToken.None);
        var req = new Iam.DomainService.Activity.RequestModel.GetActivitiesRequest { Page = 0, PageSize = 50 };

        // A signed-in user's own list filters by UserId: the event (UserId "") is not in it, not even for the redeemer.
        (await repo.GetAsync("user-9", req, CancellationToken.None)).Should().BeEmpty();

        // A client_credentials caller has no UserId, so no user filter: it lists the event. No e-mail in it.
        var listed = await repo.GetAsync(string.Empty, req, CancellationToken.None);
        var found = listed.Should().ContainSingle(x => x.Event == "signup-link-redeem").Subject;
        found.Metadata!.Keys.Should().NotContain("Email");
        JsonSerializer.Serialize(found).Should().NotContain("invitee@example.com");
    }

    // ---------- summary count (H3, C7) ----------

    private Task SeedEventAsync(string linkId, string outcome, DateTime when, string tenant = "t1", bool migrated = false)
    {
        var e = new BsonDocument
        {
            { "_id", Guid.NewGuid().ToString("n") }, { "Category", "Account" }, { "Event", "signup-link-redeem" },
            { "UserId", "" }, { "ActorUserId", "" }, { "TenantId", tenant }, { "Outcome", outcome },
            { "EntityId", linkId }, { "CreatedDate", when }, { "LastUpdatedDate", when }
        };
        if (migrated)
        {
            e["MigratedFrom"] = "SignupLinkRedemptions";
        }

        return _mongo.Database.GetCollection<BsonDocument>("UserActivitys").InsertOneAsync(e);
    }

    private Task SeedOldRowAsync(string linkId, int outcome, DateTime when, string tenant = "t1", bool reversed = false)
    {
        var row = new BsonDocument
        {
            { "_id", Guid.NewGuid().ToString("n") }, { "LinkId", linkId }, { "TenantId", tenant },
            { "Outcome", outcome }, { "RedeemedAtUtc", when }
        };
        if (reversed)
        {
            row["ReversedFromActivityId"] = "ua-1";
        }

        return _mongo.Database.GetCollection<BsonDocument>("SignupLinkRedemptions").InsertOneAsync(row);
    }

    private Task<long> CountAsync(params string[] linkIds) =>
        Repo().CountSignupLinkRejectionsAsync("t1", From, To, linkIds);

    [RealMongoFact]
    public async Task Count_EventsOnly_AfterCleanup_CountsRejectedEventsOfTheLinksInRange()
    {
        await SeedEventAsync("l1", "Rejected", Inside);
        await SeedEventAsync("l1", "Rejected", Inside);
        await SeedEventAsync("l1", "UserCreated", Inside);           // not a rejection
        await SeedEventAsync("l2", "Rejected", Inside);              // link not in list
        await SeedEventAsync("l1", "Rejected", To);                  // upper bound is exclusive
        await SeedEventAsync("l1", "Rejected", From.AddSeconds(-1)); // before range

        (await CountAsync("l1")).Should().Be(2);
    }

    [RealMongoFact]
    public async Task Count_BeforeApply_OldRowsOnly()
    {
        await SeedOldRowAsync("l1", 1, Inside);
        await SeedOldRowAsync("l1", 1, Inside);
        await SeedOldRowAsync("l1", 0, Inside);   // UserCreated
        await SeedOldRowAsync("l2", 1, Inside);

        (await CountAsync("l1")).Should().Be(2);
    }

    [RealMongoFact]
    public async Task Count_OldRowsAndNewEvents_AddUp_WithoutDoubleCount_AfterApply()
    {
        // Two redemptions: one row has been copied to a migrated event (counted once, from the row),
        // one new event was written by new code (counted from the event).
        await SeedOldRowAsync("l1", 1, Inside);
        await SeedEventAsync("l1", "Rejected", Inside, migrated: true);
        await SeedEventAsync("l1", "Rejected", Inside);

        (await CountAsync("l1")).Should().Be(2);
    }

    [RealMongoFact]
    public async Task Count_RowWrittenBackByARollback_IsNotCountedTwice()
    {
        // A rollback writes an event back as a row (ReversedFromActivityId); the event is already counted.
        await SeedOldRowAsync("l1", 1, Inside, reversed: true);
        await SeedEventAsync("l1", "Rejected", Inside);

        (await CountAsync("l1")).Should().Be(1);
    }

    [RealMongoFact]
    public async Task Count_AfterCleanup_MigratedEventsAreCounted_BecauseTheOldCollectionIsGone()
    {
        // Old collection dropped: the migrated events are the only record, so they count (the MigratedFrom
        // exclusion applies only while the old collection exists).
        await SeedEventAsync("l1", "Rejected", Inside, migrated: true);
        await SeedEventAsync("l1", "Rejected", Inside);

        (await CountAsync("l1")).Should().Be(2);
    }

    [RealMongoFact]
    public async Task Count_ForeignTenantEventAndRow_AreNotCounted()
    {
        await SeedEventAsync("l1", "Rejected", Inside, tenant: "t2");
        await SeedOldRowAsync("l1", 1, Inside, tenant: "t2");

        (await CountAsync("l1")).Should().Be(0);
    }

    [RealMongoFact]
    public async Task Count_EmptyLinkList_IsZero_AndNeverTouchesMongo()
    {
        (await CountAsync()).Should().Be(0);
        (await CountAsync("", " ")).Should().Be(0);
    }

    [RealMongoFact]
    public async Task Count_DoesNotCreateTheOldCollection()
    {
        await CountAsync("l1");

        var names = await (await _mongo.Database.ListCollectionNamesAsync()).ToListAsync();
        names.Should().NotContain("SignupLinkRedemptions");
    }
}
