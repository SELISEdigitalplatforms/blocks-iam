using System.Text.Json;
using FluentAssertions;
using Iam.DomainService.Batch;

namespace XUnitTest.IamTests.Batch
{
    /// <summary>The API-stage checks of POST iam/batch/users (spec 683 §6.2, §6.6; C1, C3), flat items.</summary>
    public class BatchRequestValidatorTests
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

        private static BatchUserItem Item(string? reference, string email, List<BatchMembership>? memberships = null) => new()
        {
            Ref = reference,
            Email = email,
            Memberships = memberships
        };

        private static BatchUsersRequest Request(params BatchUserItem[] items) => new() { Items = items.ToList() };

        [Fact]
        public void Validate_NoItems_IsRefusedWithItemsRequired()
        {
            BatchRequestValidator.Validate(new BatchUsersRequest()).RequestErrors.Should().ContainKey(BatchErrorCodes.ItemsRequired);
            BatchRequestValidator.Validate(null).RequestErrors.Should().ContainKey(BatchErrorCodes.ItemsRequired);
            BatchRequestValidator.Validate(new BatchUsersRequest { Items = [] }).RequestErrors.Should().ContainKey(BatchErrorCodes.ItemsRequired);
        }

        [Fact]
        public void Validate_101Items_IsRefusedWithItemsLimitExceeded()
        {
            var items = Enumerable.Range(0, 101).Select(i => Item($"r{i}", $"u{i}@example.com")).ToArray();

            var result = BatchRequestValidator.Validate(Request(items));

            result.RequestErrors.Should().ContainKey(BatchErrorCodes.ItemsLimitExceeded)
                .WhoseValue.Should().Be("A batch can contain at most 100 items");
            result.Accepted.Should().BeEmpty();
        }

        [Fact]
        public void Validate_100Items_IsAccepted()
        {
            var items = Enumerable.Range(0, 100).Select(i => Item($"r{i}", $"u{i}@example.com")).ToArray();

            var result = BatchRequestValidator.Validate(Request(items));

            result.RequestErrors.Should().BeNull();
            result.Accepted.Should().HaveCount(100);
        }

        [Fact]
        public void Validate_DuplicateRef_IsRefusedAndNamesTheRef()
        {
            var result = BatchRequestValidator.Validate(Request(Item("same", "a@example.com"), Item("same", "b@example.com")));

            result.RequestErrors.Should().ContainKey(BatchErrorCodes.DuplicateRef).WhoseValue.Should().Contain("same");
        }

        [Fact]
        public void Validate_RefIsOptional_AndMissingRefsAreNotDuplicates()
        {
            var result = BatchRequestValidator.Validate(Request(Item(null, "a@example.com"), Item("", "b@example.com"), Item(null, "c@example.com")));

            result.RequestErrors.Should().BeNull();
            result.Rejected.Should().BeEmpty();
            result.Accepted.Should().HaveCount(3).And.OnlyContain(a => a.Ref == null);
        }

        [Fact]
        public void Validate_EmailsDifferingOnlyByCase_AreRefusedWithBothIndexes()
        {
            var result = BatchRequestValidator.Validate(Request(
                Item("r0", "A@X.com"), Item("r1", "b@x.com"), Item("r2", "c@x.com"), Item("r3", " a@x.com ")));

            result.RequestErrors.Should().ContainKey(BatchErrorCodes.DuplicateEmail)
                .WhoseValue.Should().Be("Duplicate email in request: a@x.com (items 0, 3)");
        }

        [Fact]
        public void Validate_ClientReferenceOver200_IsRefused()
        {
            var request = Request(Item("r0", "a@example.com"));
            request.ClientReference = new string('c', 201);

            BatchRequestValidator.Validate(request).RequestErrors.Should().ContainKey(BatchErrorCodes.ClientReferenceTooLong);

            request.ClientReference = new string('c', 200);
            BatchRequestValidator.Validate(request).RequestErrors.Should().BeNull();
        }

        [Fact]
        public void Validate_InvalidItem_IsRejectedAloneAndTheOthersAccepted()
        {
            var result = BatchRequestValidator.Validate(Request(
                Item("ok-1", "ok1@example.com"), Item("bad", "not-an-email"), Item(null, "OK2@Example.com ")));

            result.RequestErrors.Should().BeNull();
            result.Accepted.Select(a => a.Index).Should().Equal(0, 2);
            result.Accepted[1].Data.Email.Should().Be("ok2@example.com");
            result.Rejected.Should().ContainSingle();
            result.Rejected[0].Index.Should().Be(1);
            result.Rejected[0].Ref.Should().Be("bad");
            result.Rejected[0].Email.Should().Be("not-an-email");
            result.Rejected[0].Errors.Should().ContainSingle(e => e.Code == BatchErrorCodes.EmailInvalid && e.Field == "email");
        }

        public static TheoryData<BatchUserItem, string, string> ItemErrors() => new()
        {
            { new BatchUserItem { Ref = new string('r', 129), Email = "a@example.com" }, "ref", BatchErrorCodes.RefTooLong },
            { new BatchUserItem { Email = " " }, "email", BatchErrorCodes.EmailRequired },
            { new BatchUserItem { Ref = "r" }, "email", BatchErrorCodes.EmailRequired },
            { new BatchUserItem { Email = new string('a', 250) + "@x.com" }, "email", BatchErrorCodes.EmailTooLong },
            { new BatchUserItem { Email = "a@example.com", FirstName = new string('f', 151) }, "firstName", BatchErrorCodes.FirstNameTooLong },
            { new BatchUserItem { Email = "a@example.com", LastName = new string('l', 151) }, "lastName", BatchErrorCodes.LastNameTooLong },
            { new BatchUserItem { Email = "a@example.com", PhoneNumber = "8801700000000" }, "phoneNumber", BatchErrorCodes.PhoneInvalid },
            { new BatchUserItem { Email = "a@example.com", PhoneNumber = "+" + new string('1', 20) }, "phoneNumber", BatchErrorCodes.PhoneInvalid },
            {
                new BatchUserItem { Email = "a@example.com", Memberships = Enumerable.Range(0, 21).Select(i => new BatchMembership { OrganizationId = $"o{i}" }).ToList() },
                "memberships", BatchErrorCodes.MembershipsLimitExceeded
            },
            {
                new BatchUserItem { Email = "a@example.com", Memberships = [new() { OrganizationId = "o1" }, new() { OrganizationId = "o1" }] },
                "memberships[1].organizationId", BatchErrorCodes.OrganizationDuplicate
            },
            {
                new BatchUserItem { Email = "a@example.com", Memberships = [new() { OrganizationId = "o1", Permissions = ["p1", "p2", "p3", "p4", "p5", "p6"] }] },
                "memberships[0].permissions", BatchErrorCodes.TooManyPermissions
            },
            { new BatchUserItem { Email = "a@example.com", Mail = new() { Send = true, Template = new string('t', 201) } }, "mail.template", BatchErrorCodes.MailTemplateTooLong },
            { new BatchUserItem { Email = "a@example.com", Mail = new() { Send = true, ExistingUserTemplate = new string('t', 201) } }, "mail.existingUserTemplate", BatchErrorCodes.MailTemplateTooLong },
            { new BatchUserItem { Email = "a@example.com", Mail = new() { Send = true, Language = new string('l', 21) } }, "mail.language", BatchErrorCodes.MailLanguageTooLong }
        };

        [Theory]
        [MemberData(nameof(ItemErrors))]
        public void Validate_ItemBasicCheck_RejectsTheItemWithFieldAndCode(BatchUserItem item, string field, string code)
        {
            var result = BatchRequestValidator.Validate(Request(item));

            result.RequestErrors.Should().BeNull();
            result.Accepted.Should().BeEmpty();
            result.Rejected.Should().ContainSingle().Which.Errors.Should().Contain(e => e.Field == field && e.Code == code && e.Message.Length > 0);
        }

        [Fact]
        public void Validate_FivePermissionsAndTwentyMemberships_AreAccepted()
        {
            var memberships = Enumerable.Range(0, 20).Select(i => new BatchMembership { OrganizationId = $"o{i}", Permissions = ["p1", "p2", "p3", "p4", "p5"] }).ToList();

            BatchRequestValidator.Validate(Request(Item("r", "a@example.com", memberships))).Accepted.Should().ContainSingle();
        }

        [Fact]
        public void Validate_FlatJsonItem_IsRead_AttributesStoredAsPlainValues_AndUnmappedFieldsDropped()
        {
            var request = JsonSerializer.Deserialize<BatchUsersRequest>(
                """
                {"clientReference":"wave-1","items":[{"ref":"legacy-1","email":"A@Example.com","firstName":"Ann",
                 "password":"secret","operation":"upsert","attributes":{"legacyUserId":"123","n":4},
                 "memberships":[{"organizationId":"o1","roles":["admin"]}]}]}
                """, Json)!;
            request.Items![0].UnmappedFields.Should().ContainKeys("password", "operation");

            var result = BatchRequestValidator.Validate(request);

            var stored = result.Accepted.Should().ContainSingle().Subject;
            stored.Ref.Should().Be("legacy-1");
            stored.Data.Email.Should().Be("a@example.com");
            stored.Data.FirstName.Should().Be("Ann");
            stored.Data.Memberships.Should().ContainSingle().Which.Roles.Should().Equal("admin");
            stored.Data.Attributes!["legacyUserId"].Should().Be("123");
            stored.Data.Attributes["legacyUserId"].Should().NotBeOfType<JsonElement>();
        }

        [Fact]
        public void Validate_OldWrappedShape_HasNoEmail_AndIsRejected()
        {
            var request = JsonSerializer.Deserialize<BatchUsersRequest>(
                """{"items":[{"ref":"r","operation":"upsert","data":{"email":"a@example.com"}}]}""", Json)!;

            var result = BatchRequestValidator.Validate(request);

            result.Rejected.Should().ContainSingle().Which.Errors.Should().ContainSingle(e => e.Code == BatchErrorCodes.EmailRequired);
        }

        [Fact]
        public void Validate_Mail_IsStoredOnlyWhenSendIsTrue_Trimmed()
        {
            var result = BatchRequestValidator.Validate(Request(
                new BatchUserItem { Email = "a@example.com", Mail = new() { Send = true, Template = " Welcome ", ExistingUserTemplate = " ", Language = "de-DE" } },
                new BatchUserItem { Email = "b@example.com", Mail = new() { Send = false, Template = "Welcome" } },
                new BatchUserItem { Email = "c@example.com" }));

            var mail = result.Accepted[0].Data.Mail!;
            mail.Send.Should().BeTrue();
            mail.Template.Should().Be("Welcome");
            mail.ExistingUserTemplate.Should().BeNull();
            mail.Language.Should().Be("de-DE");
            result.Accepted[1].Data.Mail.Should().BeNull();
            result.Accepted[2].Data.Mail.Should().BeNull();
        }

        [Fact]
        public void Validate_MailWithoutTemplate_IsAccepted_TheMailPhaseSkipsIt()
        {
            var result = BatchRequestValidator.Validate(Request(new BatchUserItem { Email = "a@example.com", Mail = new() { Send = true } }));

            result.Rejected.Should().BeEmpty();
            result.Accepted.Should().ContainSingle().Which.Data.Mail!.Template.Should().BeNull();
        }

        [Fact]
        public void Validate_NullMembershipEntry_KeepsItsPosition()
        {
            var result = BatchRequestValidator.Validate(Request(Item("r", "a@example.com", [null!, new BatchMembership { OrganizationId = "o1", Roles = ["", "admin"] }])));

            var memberships = result.Accepted.Should().ContainSingle().Subject.Data.Memberships!;
            memberships.Should().HaveCount(2);
            memberships[1].OrganizationId.Should().Be("o1");
            memberships[1].Roles.Should().Equal("admin");
        }
    }
}
