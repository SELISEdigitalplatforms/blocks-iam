using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;

namespace Iam.DomainService.Services
{
    public interface IUserActivityDispatcher
    {
        Task SendUserActivityAsync(UserActivityEvent evt);
    }
}