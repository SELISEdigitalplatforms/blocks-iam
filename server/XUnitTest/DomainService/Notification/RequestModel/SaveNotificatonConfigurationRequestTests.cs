using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.Enums;
using Xunit;

namespace XUnitTest.DomainService.Notification.RequestModel
{
    public class SaveNotificatonConfigurationRequestTests
    {
        [Fact]
        public void Can_Set_And_Get_All_Properties()
        {
            var req = new SaveNotificatonConfigurationRequest
            {
                Name = "TestName",
                ChannelToNotify = NotifierTypes.Firebase,
                NotificationType = NotificationReceiverTypes.UserSpecificReceiverType,
                EnablePersistence = true,
                NotifyMethod = "TestMethod",
                ProjectKey = "project-4",
                IsUpdateRequest = true
            };
            Assert.Equal("TestName", req.Name);
            Assert.Equal(NotifierTypes.Firebase, req.ChannelToNotify);
            Assert.Equal(NotificationReceiverTypes.UserSpecificReceiverType, req.NotificationType);
            Assert.True(req.EnablePersistence);
            Assert.Equal("TestMethod", req.NotifyMethod);
            Assert.Equal("project-4", req.ProjectKey);
            Assert.True(req.IsUpdateRequest);
        }

        [Fact]
        public void Default_Values_Are_Null_Or_False()
        {
            var req = new SaveNotificatonConfigurationRequest();
            Assert.Null(req.Name);
            Assert.Equal(default(NotifierTypes), req.ChannelToNotify);
            Assert.Equal(default(NotificationReceiverTypes), req.NotificationType);
            Assert.False(req.EnablePersistence);
            Assert.Null(req.NotifyMethod);
            Assert.Null(req.ProjectKey);
            Assert.False(req.IsUpdateRequest);
        }
    }
}
