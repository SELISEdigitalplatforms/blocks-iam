using Authentication.DomainService.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace XUnitTest.Auth
{
    public class PasswordHasherTests
    {
        private static PasswordHasher CreateHasher() => new(NullLogger<PasswordHasher>.Instance);

        [Fact]
        public void Verify_ReturnsFalse_WhenPasswordIsNull()
        {
            var hasher = CreateHasher();
            hasher.Verify(null, BCrypt.Net.BCrypt.HashPassword("p")).Should().BeFalse();
        }

        [Fact]
        public void Verify_ReturnsFalse_WhenPasswordIsEmpty()
        {
            var hasher = CreateHasher();
            hasher.Verify(string.Empty, BCrypt.Net.BCrypt.HashPassword("p")).Should().BeFalse();
        }

        [Fact]
        public void Verify_ReturnsFalse_WhenPasswordIsWhitespace()
        {
            var hasher = CreateHasher();
            hasher.Verify("   ", BCrypt.Net.BCrypt.HashPassword("p")).Should().BeFalse();
        }

        [Fact]
        public void Verify_ReturnsFalse_WhenHashIsNull()
        {
            var hasher = CreateHasher();
            hasher.Verify("password", null).Should().BeFalse();
        }

        [Fact]
        public void Verify_ReturnsFalse_WhenHashIsEmpty()
        {
            var hasher = CreateHasher();
            hasher.Verify("password", string.Empty).Should().BeFalse();
        }

        [Fact]
        public void Verify_ReturnsFalse_WhenHashIsInvalid()
        {
            var hasher = CreateHasher();
            hasher.Verify("password", "not-a-bcrypt-hash").Should().BeFalse();
        }

        [Fact]
        public void Verify_ReturnsTrue_ForMatchingPassword()
        {
            var hasher = CreateHasher();
            var hash = BCrypt.Net.BCrypt.HashPassword("Secret123!");
            hasher.Verify("Secret123!", hash).Should().BeTrue();
        }

        [Fact]
        public void Verify_ReturnsFalse_ForWrongPassword()
        {
            var hasher = CreateHasher();
            var hash = BCrypt.Net.BCrypt.HashPassword("Secret123!");
            hasher.Verify("WrongPassword", hash).Should().BeFalse();
        }

        [Fact]
        public void Verify_WithSalt_MatchesSaltedHash()
        {
            var hasher = CreateHasher();
            var salt = "tenant-salt-001";
            var hash = BCrypt.Net.BCrypt.HashPassword($"Secret123!::{salt}");
            hasher.Verify("Secret123!", hash, salt).Should().BeTrue();
        }

        [Fact]
        public void Verify_WithSalt_FailsWithoutSalt()
        {
            var hasher = CreateHasher();
            var salt = "tenant-salt-001";
            var hash = BCrypt.Net.BCrypt.HashPassword($"Secret123!::{salt}");
            hasher.Verify("Secret123!", hash, null).Should().BeFalse();
        }

        [Fact]
        public void Verify_WithDifferentSalt_Fails()
        {
            var hasher = CreateHasher();
            var hash = BCrypt.Net.BCrypt.HashPassword("Secret123!::salt-A");
            hasher.Verify("Secret123!", hash, "salt-B").Should().BeFalse();
        }
    }
}