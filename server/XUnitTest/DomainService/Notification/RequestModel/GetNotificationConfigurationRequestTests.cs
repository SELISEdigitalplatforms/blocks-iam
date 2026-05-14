using CloudConfiguration.DomainService.Notification.RequestModel;
using Xunit;

namespace XUnitTest.DomainService.Notification.RequestModel
{
    public class GetNotificationConfigurationRequestTests
    {
        [Fact]
        public void Can_Set_And_Get_Properties()
        {
            var req = new GetNotificationConfigurationRequest
            {
                ItemId = "item-2",
                ProjectKey = "project-2"
            };
            Assert.Equal("item-2", req.ItemId);
            Assert.Equal("project-2", req.ProjectKey);
        }

        [Fact]
        public void Default_Values_Are_Null()
        {
            var req = new GetNotificationConfigurationRequest();
            Assert.Null(req.ItemId);
            Assert.Null(req.ProjectKey);
        }
    }
}
