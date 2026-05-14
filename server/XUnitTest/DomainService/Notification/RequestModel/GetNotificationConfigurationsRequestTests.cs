using CloudConfiguration.DomainService.Notification.RequestModel;
using Xunit;

namespace XUnitTest.DomainService.Notification.RequestModel
{
    public class GetNotificationConfigurationsRequestTests
    {
        [Fact]
        public void Can_Set_And_Get_ProjectKey()
        {
            var req = new GetNotificationConfigurationsRequest
            {
                ProjectKey = "project-3"
            };
            Assert.Equal("project-3", req.ProjectKey);
        }

        [Fact]
        public void Default_ProjectKey_Is_Null()
        {
            var req = new GetNotificationConfigurationsRequest();
            Assert.Null(req.ProjectKey);
        }
    }
}
