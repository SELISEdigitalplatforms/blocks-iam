using FluentAssertions;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Moq;

namespace XUnitTest.IamTests.Resources
{
    // Exercises the shared rules defined in the generic BasePermissionValidator<T>
    // through its two concrete subclasses: CreatePermissionValidator and
    // UpdatePermissionValidator (which also add the resource-uniqueness rule).
    public class BasePermissionValidatorTests
    {
        private readonly Mock<IResourceRepository> _resourceRepository = new();
        private readonly Mock<IIdentityAccessManagementService> _iam = new();

        private CreatePermissionValidator CreateValidator() =>
            new(_resourceRepository.Object, _iam.Object);

        private UpdatePermissionValidator UpdateValidator() =>
            new(_resourceRepository.Object, _iam.Object);

        private static CreatePermissionRequest ValidCreateRequest() => new()
        {
            Name = "Read Reports",
            Type = ResourceType.FrontendAction,
            Resource = "reports.read",
            ResourceGroup = "reporting",
            IsBuiltIn = false
        };

        [Fact]
        public async Task Valid_Request_Passes()
        {
            var result = await CreateValidator().ValidateAsync(ValidCreateRequest());
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task Name_Empty_Fails()
        {
            var req = ValidCreateRequest();
            req.Name = "";

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(req.Name));
        }

        [Fact]
        public async Task Name_TooLong_Fails_WithCustomMessage()
        {
            var req = ValidCreateRequest();
            req.Name = new string('a', 151);

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Maximum character limit 150 exceeded");
        }

        [Fact]
        public async Task Type_None_Fails()
        {
            var req = ValidCreateRequest();
            req.Type = ResourceType.None;

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(req.Type));
        }

        [Fact]
        public async Task Type_UndefinedEnum_Fails()
        {
            var req = ValidCreateRequest();
            req.Type = (ResourceType)999;

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(req.Type));
        }

        [Fact]
        public async Task Resource_Empty_Fails()
        {
            var req = ValidCreateRequest();
            req.Resource = "";

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(req.Resource));
        }

        [Fact]
        public async Task Resource_WithSpaces_Fails_WithCustomMessage()
        {
            var req = ValidCreateRequest();
            req.Resource = "reports read";

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Resource cannot contain spaces.");
        }

        [Fact]
        public async Task ResourceGroup_Empty_Fails()
        {
            var req = ValidCreateRequest();
            req.ResourceGroup = "";

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(req.ResourceGroup));
        }

        [Fact]
        public async Task ResourceGroup_WithSpaces_Fails_WithCustomMessage()
        {
            var req = ValidCreateRequest();
            req.ResourceGroup = "report group";

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "ResourceGroup must not contain spaces.");
        }

        [Fact]
        public async Task IsBuiltIn_True_AndNotRoot_Fails()
        {
            _iam.Setup(s => s.IsRoot()).Returns(false);
            var req = ValidCreateRequest();
            req.IsBuiltIn = true;

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "You are not allowed");
        }

        [Fact]
        public async Task IsBuiltIn_True_AndRoot_Passes()
        {
            _iam.Setup(s => s.IsRoot()).Returns(true);
            var req = ValidCreateRequest();
            req.IsBuiltIn = true;

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task Endpoint_Type_WithInvalidStructure_Fails()
        {
            var req = ValidCreateRequest();
            req.Type = ResourceType.Endpoint;
            req.Resource = "badresource"; // no spaces so passes the resource rule, but not Service::Controller::Action

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e =>
                e.ErrorMessage == "Endpoint resource must be in the format Service::Controller::Action");
        }

        [Fact]
        public async Task Endpoint_Type_WithValidStructure_Passes()
        {
            var req = ValidCreateRequest();
            req.Type = ResourceType.Endpoint;
            req.Resource = "OrderService::OrderController::Create";

            var result = await CreateValidator().ValidateAsync(req);

            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task Create_ExistingResource_Fails()
        {
            _resourceRepository
                .Setup(r => r.GetPermissionByResourceAsync(It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new Permission { ItemId = "existing-perm" });

            var result = await CreateValidator().ValidateAsync(ValidCreateRequest());

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Resource_Already_Exists");
        }

        [Fact]
        public async Task Update_ExistingResource_WithDifferentItemId_Fails()
        {
            _resourceRepository
                .Setup(r => r.GetPermissionByResourceAsync(It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new Permission { ItemId = "other-perm" });

            var req = new UpdatePermissionRequest
            {
                Name = "Read Reports",
                Type = ResourceType.FrontendAction,
                Resource = "reports.read",
                ResourceGroup = "reporting",
                ItemId = "this-perm"
            };

            var result = await UpdateValidator().ValidateAsync(req);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.ErrorMessage == "Resource_Already_Exists");
        }

        [Fact]
        public async Task Update_ExistingResource_WithSameItemId_Passes()
        {
            _resourceRepository
                .Setup(r => r.GetPermissionByResourceAsync(It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new Permission { ItemId = "this-perm" });

            var req = new UpdatePermissionRequest
            {
                Name = "Read Reports",
                Type = ResourceType.FrontendAction,
                Resource = "reports.read",
                ResourceGroup = "reporting",
                ItemId = "this-perm"
            };

            var result = await UpdateValidator().ValidateAsync(req);

            result.IsValid.Should().BeTrue();
        }
    }
}
