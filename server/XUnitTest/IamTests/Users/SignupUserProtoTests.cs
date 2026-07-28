using FluentAssertions;
using Google.Protobuf;
using Iam.DomainService.Users;

namespace XUnitTest.IamTests.Users
{
    /// <summary>
    /// Unit tests for the generated gRPC contract messages <see cref="SignupUserRequest"/> and
    /// <see cref="SignupUserReply"/>. These assert real serialization behavior: a message round-tripped
    /// through its wire format must equal the original, clones must be independent-but-equal, optional
    /// fields must track presence, and map fields must survive encoding.
    /// </summary>
    public sealed class SignupUserProtoTests
    {
        private static SignupUserRequest FullRequest() => new()
        {
            Email = "user@example.com",
            UserName = "user",
            FirstName = "First",
            LastName = "Last",
            MailPurpose = "signup",
            Platform = "web",
            ProjectKey = "proj-1"
        };

        [Fact]
        public void SignupUserRequest_RoundTripsThroughWireFormat()
        {
            var original = FullRequest();

            var bytes = original.ToByteArray();
            var parsed = SignupUserRequest.Parser.ParseFrom(bytes);

            parsed.Should().Be(original);
            parsed.Email.Should().Be("user@example.com");
            parsed.ProjectKey.Should().Be("proj-1");
        }

        [Fact]
        public void SignupUserRequest_Clone_IsEqualButDistinct()
        {
            var original = FullRequest();
            var clone = original.Clone();

            clone.Should().Be(original);
            clone.Should().NotBeSameAs(original);
            clone.GetHashCode().Should().Be(original.GetHashCode());
            original.CalculateSize().Should().BeGreaterThan(0);
        }

        [Fact]
        public void SignupUserRequest_OptionalFields_TrackPresence()
        {
            var request = new SignupUserRequest { Email = "e@x.com" };
            request.HasUserName.Should().BeFalse();

            request.UserName = "u";
            request.HasUserName.Should().BeTrue();

            request.ClearUserName();
            request.HasUserName.Should().BeFalse();
        }

        [Fact]
        public void SignupUserRequest_InequalityAndToString()
        {
            var a = FullRequest();
            var b = FullRequest();
            b.Email = "other@x.com";

            a.Equals(b).Should().BeFalse();
            a.ToString().Should().Contain("user@example.com");
        }

        [Fact]
        public void SignupUserReply_RoundTripsWithErrorsMap()
        {
            var original = new SignupUserReply { ItemId = "u1", IsSuccess = false };
            original.Errors.Add("email", "already exists");
            original.Errors.Add("username", "taken");

            var parsed = SignupUserReply.Parser.ParseFrom(original.ToByteArray());

            parsed.Should().Be(original);
            parsed.IsSuccess.Should().BeFalse();
            parsed.ItemId.Should().Be("u1");
            parsed.Errors.Should().ContainKey("email").WhoseValue.Should().Be("already exists");
            parsed.Errors.Should().HaveCount(2);
        }

        [Fact]
        public void SignupUserReply_SuccessClone_IsEqual()
        {
            var reply = new SignupUserReply { ItemId = "u2", IsSuccess = true };
            var clone = reply.Clone();

            clone.Should().Be(reply);
            clone.CalculateSize().Should().BeGreaterThan(0);
            reply.HasItemId.Should().BeTrue();
            reply.ClearItemId();
            reply.HasItemId.Should().BeFalse();
        }
    }
}
