using FluentAssertions;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Moq;

namespace XUnitTest.Mfa.Shared.Services
{
    public class MfaBackupCodeServiceTests
    {
        private static MfaBackupCodeService CreateService(out Mock<IMfaManagementRepository> repo)
        {
            repo = new Mock<IMfaManagementRepository>();
            return new MfaBackupCodeService(repo.Object);
        }

        [Fact]
        public async Task GenerateAsync_WhenUserIdEmpty_ReturnsError()
        {
            var service = CreateService(out _);
            var result = await service.GenerateAsync("", 5);
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("empty_user_id");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task GenerateAsync_WhenCountZeroOrNegative_ReturnsError(int count)
        {
            var service = CreateService(out _);
            var result = await service.GenerateAsync("user-1", count);
            result.Errors.Should().ContainKey("invalid_count");
        }

        [Fact]
        public async Task GenerateAsync_WhenCountAbove50_ReturnsError()
        {
            var service = CreateService(out _);
            var result = await service.GenerateAsync("user-1", 51);
            result.Errors.Should().ContainKey("invalid_count");
        }

        [Fact]
        public async Task GenerateAsync_RevokesExistingCodesBeforeGenerating()
        {
            var service = CreateService(out var repo);
            var saved = new List<MfaBackupCode>();
            repo.Setup(r => r.SaveAsync(It.IsAny<MfaBackupCode>(), It.IsAny<string>()))
                .Callback<MfaBackupCode, string>((c, _) => saved.Add(c))
                .Returns(Task.CompletedTask);
            repo.Setup(r => r.DeleteItemsAsync<MfaBackupCode>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>>()))
                .Returns(Task.CompletedTask);

            var result = await service.GenerateAsync("user-1", 3);

            result.IsSuccess.Should().BeTrue();
            result.PlainCodes.Should().HaveCount(3);
            saved.Should().HaveCount(3);
            repo.Verify(r => r.DeleteItemsAsync<MfaBackupCode>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>>()), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_PersistsHashAndPrefix_AndReturnsPlainCodes()
        {
            var service = CreateService(out var repo);
            MfaBackupCode? captured = null;
            repo.Setup(r => r.SaveAsync(It.IsAny<MfaBackupCode>(), It.IsAny<string>()))
                .Callback<MfaBackupCode, string>((c, _) => captured = c)
                .Returns(Task.CompletedTask);

            var result = await service.GenerateAsync("user-1", 1);

            result.IsSuccess.Should().BeTrue();
            result.PlainCodes.Should().HaveCount(1);
            var code = result.PlainCodes[0];
            code.Should().MatchRegex("^[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}$");
            captured.Should().NotBeNull();
            captured!.UserId.Should().Be("user-1");
            captured.CodePrefix.Should().Be(code[..4]);
            captured.CodeHash.Should().NotBeNullOrEmpty();
            captured.IsUsed.Should().BeFalse();
        }

        [Fact]
        public async Task ConsumeAsync_WhenUserIdOrCodeEmpty_ReturnsError()
        {
            var service = CreateService(out _);
            var r1 = await service.ConsumeAsync("", "code");
            r1.Errors.Should().ContainKey("invalid_request");

            var r2 = await service.ConsumeAsync("user-1", "");
            r2.Errors.Should().ContainKey("invalid_request");
        }

        [Fact]
        public async Task ConsumeAsync_WhenNoMatchingCode_ReturnsInvalid()
        {
            var service = CreateService(out var repo);
            repo.Setup(r => r.GetItemsAsync<MfaBackupCode>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new List<MfaBackupCode>());

            var result = await service.ConsumeAsync("user-1", "abcd-efgh-ijkl-mnop");

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("invalid_code");
        }

        [Fact]
        public async Task ConsumeAsync_OnMatch_MarksUsedAndUpserts()
        {
            var service = CreateService(out var repo);
            var code = "abcd-ef01-2345-6789";
            var normalized = code.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
            var stored = new MfaBackupCode
            {
                UserId = "user-1",
                CodeHash = HashForTest(normalized),
                CodePrefix = code[..4],
                IsUsed = false
            };
            repo.Setup(r => r.GetItemsAsync<MfaBackupCode>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(new List<MfaBackupCode> { stored });
            MfaBackupCode? upserted = null;
            repo.Setup(r => r.UpsertAsync(It.IsAny<MfaBackupCode>(), It.IsAny<System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>>(), It.IsAny<string>()))
                .Callback<MfaBackupCode, System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>, string>((c, _, _) => upserted = c)
                .Returns(Task.CompletedTask);

            var result = await service.ConsumeAsync("user-1", code);

            result.IsValid.Should().BeTrue();
            result.UserId.Should().Be("user-1");
            upserted.Should().NotBeNull();
            upserted!.IsUsed.Should().BeTrue();
            upserted.UsedAtUtc.Should().NotBeNull();
            upserted.LastUpdatedBy.Should().Be("user-1");
        }

        [Fact]
        public async Task RevokeAllAsync_WhenUserIdEmpty_NoOp()
        {
            var service = CreateService(out var repo);
            await service.RevokeAllAsync("");
            repo.Verify(r => r.DeleteItemsAsync<MfaBackupCode>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>>()), Times.Never);
        }

        [Fact]
        public async Task RevokeAllAsync_DelegatesDeleteToRepository()
        {
            var service = CreateService(out var repo);
            repo.Setup(r => r.DeleteItemsAsync<MfaBackupCode>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>>()))
                .Returns(Task.CompletedTask);

            await service.RevokeAllAsync("user-1");

            repo.Verify(r => r.DeleteItemsAsync<MfaBackupCode>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>>()), Times.Once);
        }

        [Fact]
        public async Task GetRemainingCountAsync_WhenUserIdEmpty_ReturnsZero()
        {
            var service = CreateService(out _);
            var count = await service.GetRemainingCountAsync("");
            count.Should().Be(0);
        }

        [Fact]
        public async Task GetRemainingCountAsync_ReturnsCountOfUnusedCodes()
        {
            var service = CreateService(out var repo);
            var unusedCodes = new List<MfaBackupCode>
            {
                new() { UserId = "user-1", IsUsed = false },
                new() { UserId = "user-1", IsUsed = false }
            };
            repo.Setup(r => r.GetItemsAsync<MfaBackupCode>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync(unusedCodes);

            var count = await service.GetRemainingCountAsync("user-1");

            count.Should().Be(2);
            repo.Verify(r => r.GetItemsAsync<MfaBackupCode>(It.IsAny<System.Linq.Expressions.Expression<Func<MfaBackupCode, bool>>>(), It.IsAny<string>()), Times.Once);
        }

        private static string HashForTest(string normalizedCode)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalizedCode));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
