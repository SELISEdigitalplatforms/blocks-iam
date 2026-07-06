namespace Blocks.CaptchaDriver;

/// <summary>
/// Sends HTTP requests for captcha verification. Implementations must use
/// <see cref="IHttpClientFactory"/> to manage <see cref="HttpClient"/> lifetimes.
/// </summary>
public interface IHttpClientService
{
    /// <summary>
    /// Sends an HTTP request using a managed <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="contentType">Content type value applied as a default request header.</param>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage? request, string? contentType);
}
