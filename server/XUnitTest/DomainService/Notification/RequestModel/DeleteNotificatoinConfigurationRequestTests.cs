using CloudConfiguration.DomainService.Notification.RequestModel;
using Xunit;

namespace XUnitTest.DomainService.Notification.RequestModel
{
    public class DeleteNotificatoinConfigurationRequestTests
    {
        [Fact]
        public void Can_Set_And_Get_Properties()
        {
            var req = new DeleteNotificatoinConfigurationRequest
            {
                ItemId = "item-1",
                ProjectKey = "project-1"
            };
            Assert.Equal("item-1", req.ItemId);
            Assert.Equal("project-1", req.ProjectKey);
        }

        [Fact]
        public void Default_Values_Are_Null()
        {
            var req = new DeleteNotificatoinConfigurationRequest();
            Assert.Null(req.ItemId);
            Assert.Null(req.ProjectKey);
        }
    }
}
