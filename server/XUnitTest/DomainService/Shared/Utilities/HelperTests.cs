using System;
using CloudConfiguration.DomainService.Shared.Utilities;
using FluentAssertions;
using Xunit;

namespace CloudConfiguration.DomainService.Shared.Utilities.Tests
{
    public class HelperTests
    {
        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("a", "*")]
        [InlineData("ab", "**")]
        [InlineData("abc", "a*c")]
        [InlineData("abcd", "a**d")]
        [InlineData("abcdef", "a****f")]
        public void GetMaskedCloudStorageRegionEndPoint_MasksCorrectly(string input, string expected)
        {
            Helper.GetMaskedCloudStorageRegionEndPoint(input).Should().Be(expected);
        }

        [Fact]
        public void GenerateAesKey_ReturnsValidBase64KeyOf32Bytes()
        {
            var key = Helper.GenerateAesKey();
            var bytes = Convert.FromBase64String(key);
            bytes.Length.Should().Be(32);
        }

        [Fact]
        public void Encrypt_And_TryDecrypt_RoundTrip_Success()
        {
            var key = Helper.GenerateAesKey();
            var plainText = "Sensitive data!";
            var cipher = Helper.Encrypt(plainText, key);
            Helper.TryDecrypt(cipher, key, out var decrypted).Should().BeTrue();
            decrypted.Should().Be(plainText);
        }

        [Fact]
        public void TryDecrypt_WithWrongKey_ReturnsFalse()
        {
            var key = Helper.GenerateAesKey();
            var wrongKey = Helper.GenerateAesKey();
            var plainText = "Secret";
            var cipher = Helper.Encrypt(plainText, key);
            Helper.TryDecrypt(cipher, wrongKey, out var decrypted).Should().BeFalse();
            decrypted.Should().Be("");
        }

        [Fact]
        public void TryDecrypt_WithInvalidCipher_ReturnsFalse()
        {
            var key = Helper.GenerateAesKey();
            Helper.TryDecrypt("notbase64", key, out var decrypted).Should().BeFalse();
            decrypted.Should().Be("");
        }
    }
}
