using Authentication.DomainService.Oidc.Services;
using FluentAssertions;

namespace XUnitTest.Auth.Oidc
{
    public class DeviceCodeGeneratorTests
    {
        private readonly DeviceCodeGenerator _generator = new();

        [Fact]
        public void GenerateDeviceCode_ReturnsNonEmptyBase64Url()
        {
            var code = _generator.GenerateDeviceCode();

            code.Should().NotBeNullOrEmpty();
            code.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
        }

        [Fact]
        public void GenerateDeviceCode_IsUnique()
        {
            var samples = Enumerable.Range(0, 200).Select(_ => _generator.GenerateDeviceCode()).ToHashSet();
            samples.Count.Should().Be(200);
        }

        [Fact]
        public void GenerateUserCode_HasExpectedShapeAndAlphabet()
        {
            for (var i = 0; i < 100; i++)
            {
                var code = _generator.GenerateUserCode();
                code.Length.Should().Be(9);
                code[4].Should().Be('-');
                var alphabet = "BCDFGHJKMPQRTVWXY2346789";
                foreach (var c in code)
                {
                    if (c == '-') continue;
                    alphabet.Should().Contain(c.ToString(), $"character {c} is not in the unambiguous alphabet");
                }
                code.Should().NotContain("0");
                code.Should().NotContain("O");
                code.Should().NotContain("1");
                code.Should().NotContain("I");
                code.Should().NotContain("L");
            }
        }

        [Fact]
        public void GenerateUserCode_IsReasonablyUnique()
        {
            var samples = Enumerable.Range(0, 1000).Select(_ => _generator.GenerateUserCode()).ToHashSet();
            samples.Count.Should().BeGreaterThan(990);
        }

        [Fact]
        public void HashDeviceCode_IsDeterministicAndLowercaseHex()
        {
            var code = _generator.GenerateDeviceCode();
            var h1 = _generator.HashDeviceCode(code);
            var h2 = _generator.HashDeviceCode(code);

            h1.Should().Be(h2);
            h1.Length.Should().Be(64);
            h1.Should().MatchRegex("^[0-9a-f]{64}$");
        }

        [Fact]
        public void HashDeviceCode_DiffersForDifferentInputs()
        {
            var h1 = _generator.HashDeviceCode("code-a");
            var h2 = _generator.HashDeviceCode("code-b");
            h1.Should().NotBe(h2);
        }
    }
}