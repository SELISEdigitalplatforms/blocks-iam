namespace Blocks.CaptchaDriver
{
    public interface IHttpClientService
    {
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string contentType);
    }
}