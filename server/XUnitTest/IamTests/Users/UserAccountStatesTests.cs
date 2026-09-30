using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Utilities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// <see cref="UserAccountStates"/> states one rule twice -- in memory for the list's
    /// <c>accountState</c> column, and as a MongoDB filter. The column and the filter must never
    /// disagree, so besides the rule itself these tests render the filter and evaluate it against
    /// every combination of the stored fields, including the fields an old document lacks.
    /// </summary>
    public class UserAccountStatesTests
    {
        private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        // ---------- Resolve ----------

        [Theory]
        [InlineData(true, UserLifecycleStatus.Active, true, UserAccountState.Active)]
        [InlineData(false, UserLifecycleStatus.PendingVerification, false, UserAccountState.PendingVerification)]
        [InlineData(false, UserLifecycleStatus.Suspended, true, UserAccountState.Suspended)]
        [InlineData(false, UserLifecycleStatus.Disabled, true, UserAccountState.Deactivated)]
        // An invited user can be created inactive with Status already Active: never verified, so pending.
        [InlineData(false, UserLifecycleStatus.Active, false, UserAccountState.PendingVerification)]
        // Inactive after having been verified, with a Status that never moved: deactivated.
        [InlineData(false, UserLifecycleStatus.Active, true, UserAccountState.Deactivated)]
        // A stored terminal status wins over the flag.
        [InlineData(true, UserLifecycleStatus.Disabled, true, UserAccountState.Deactivated)]
        [InlineData(true, UserLifecycleStatus.Suspended, true, UserAccountState.Suspended)]
        public void Resolve_FollowsTheDocumentedRule(bool active, UserLifecycleStatus status, bool isVerified, UserAccountState expected)
        {
            UserAccountStates.Resolve(active, status, isVerified).Should().Be(expected);
        }

        // ---------- BuildFilter: inputs that add no clause ----------

        [Fact]
        public void BuildFilter_Null_AddsNoClause() =>
            UserAccountStates.BuildFilter(null, Now).Should().BeNull();

        [Fact]
        public void BuildFilter_Empty_AddsNoClause() =>
            UserAccountStates.BuildFilter([], Now).Should().BeNull();

        [Fact]
        public void BuildFilter_OnlyUnrecognisedValues_AddsNoClause() =>
            UserAccountStates.BuildFilter(["Verified", " ", "42"], Now).Should().BeNull();

        // ---------- BuildFilter agrees with Resolve ----------

        public static IEnumerable<object[]> StoredShapes()
        {
            bool?[] flags = [true, false, null]; // null: the field is absent from the document
            UserLifecycleStatus?[] statuses =
            [
                UserLifecycleStatus.Active, UserLifecycleStatus.PendingVerification,
                UserLifecycleStatus.Suspended, UserLifecycleStatus.Disabled, null
            ];

            foreach (var active in flags)
                foreach (var status in statuses)
                    foreach (var isVerified in flags)
                        yield return [active, status, isVerified];
        }

        [Theory]
        [MemberData(nameof(StoredShapes))]
        public void BuildFilter_MatchesExactlyTheUsersResolveShowsInThatState(bool? active, UserLifecycleStatus? status, bool? isVerified)
        {
            var document = Document(active, status, isVerified, lockoutUntilUtc: null);
            // What the list shows: an absent field deserialises to its default -- false for the
            // flags, Active for Status (the initialiser on User.Status and GetAccounts.Status).
            var shown = UserAccountStates.Resolve(active ?? false, status ?? UserLifecycleStatus.Active, isVerified ?? false);

            foreach (var state in Enum.GetValues<UserAccountState>())
            {
                Matches(Render([state.ToString()]), document).Should().Be(
                    state == shown,
                    $"a document with Active={Show(active)}, Status={Show(status)}, IsVerified={Show(isVerified)} is shown as {shown}");
            }
        }

        [Theory]
        [InlineData(1, true)]    // an hour from now: locked out
        [InlineData(-1, false)]  // an hour ago: the lockout has lapsed
        public void BuildFilter_LockedOut_MatchesOnlyAFutureLockout(int hoursFromNow, bool expected)
        {
            var document = Document(true, UserLifecycleStatus.Active, true, Now.AddHours(hoursFromNow));

            Matches(Render([UserAccountStates.LockedOut]), document).Should().Be(expected);
        }

        [Fact]
        public void BuildFilter_LockedOut_DoesNotMatchAUserNeverLockedOut()
        {
            Matches(Render([UserAccountStates.LockedOut]), Document(true, UserLifecycleStatus.Active, true, null))
                .Should().BeFalse();
        }

        [Fact]
        public void BuildFilter_SeveralStates_MatchesAUserInAnyOfThem()
        {
            var filter = Render(["Suspended", "LockedOut"]);

            Matches(filter, Document(false, UserLifecycleStatus.Suspended, true, null)).Should().BeTrue();
            Matches(filter, Document(true, UserLifecycleStatus.Active, true, Now.AddHours(1))).Should().BeTrue();
            Matches(filter, Document(true, UserLifecycleStatus.Active, true, null)).Should().BeFalse();
        }

        [Fact]
        public void BuildFilter_IsCaseInsensitiveAndIgnoresUnknownValuesAlongsideKnownOnes()
        {
            var filter = Render(["pendingverification", "Verified"]);

            Matches(filter, Document(false, UserLifecycleStatus.PendingVerification, false, null)).Should().BeTrue();
            Matches(filter, Document(true, UserLifecycleStatus.Active, true, null)).Should().BeFalse();
        }

        // ---------- helpers ----------

        private static string Show(object? value) => value?.ToString() ?? "absent";

        private static BsonDocument Render(IEnumerable<string> states)
        {
            var filter = UserAccountStates.BuildFilter(states, Now);
            filter.Should().NotBeNull();
            var registry = BsonSerializer.SerializerRegistry;
            return filter!.Render(new RenderArgs<User>(registry.GetSerializer<User>(), registry));
        }

        /// <summary>
        /// A user as MongoDB stores it -- serialized with the same serializer the filter was
        /// rendered with, so field names and the enum's representation line up -- with the fields
        /// passed as <c>null</c> removed, the shape of a document written before they existed.
        /// </summary>
        private static BsonDocument Document(bool? active, UserLifecycleStatus? status, bool? isVerified, DateTime? lockoutUntilUtc)
        {
            var user = new User
            {
                ItemId = "u1",
                Active = active ?? false,
                Status = status ?? UserLifecycleStatus.Active,
                IsVerified = isVerified ?? false,
                LockoutUntilUtc = lockoutUntilUtc,
            };
            var document = user.ToBsonDocument();

            if (active is null) document.Remove(ElementName(nameof(User.Active)));
            if (status is null) document.Remove(ElementName(nameof(User.Status)));
            if (isVerified is null) document.Remove(ElementName(nameof(User.IsVerified)));
            return document;
        }

        private static string ElementName(string memberName) =>
            BsonClassMap.LookupClassMap(typeof(User)).GetMemberMap(memberName).ElementName;

        /// <summary>
        /// Evaluates a rendered filter against a document, for exactly the operators
        /// <see cref="UserAccountStates.BuildFilter"/> emits, with MongoDB's semantics for an absent
        /// field: it equals nothing, satisfies <c>$ne</c> and <c>$exists: false</c>, and is never
        /// <c>$gt</c> anything. Anything else fails loudly rather than guessing.
        /// </summary>
        private static bool Matches(BsonDocument filter, BsonDocument document) =>
            filter.Elements.All(element => element.Name switch
            {
                "$or" => element.Value.AsBsonArray.Any(part => Matches(part.AsBsonDocument, document)),
                "$and" => element.Value.AsBsonArray.All(part => Matches(part.AsBsonDocument, document)),
                _ => FieldMatches(element.Value, document.TryGetValue(element.Name, out var value) ? value : null),
            });

        private static bool FieldMatches(BsonValue condition, BsonValue? value)
        {
            if (condition is not BsonDocument operators || !operators.Names.All(n => n.StartsWith('$')))
            {
                return value is not null && value.Equals(condition);
            }

            return operators.Elements.All(op => op.Name switch
            {
                "$ne" => value is null || !value.Equals(op.Value),
                "$exists" => (value is not null) == op.Value.ToBoolean(),
                "$gt" => value is not null && !value.IsBsonNull && value.CompareTo(op.Value) > 0,
                _ => throw new NotSupportedException($"The test evaluator does not know {op.Name}.")
            });
        }
    }
}
