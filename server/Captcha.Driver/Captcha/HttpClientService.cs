namespace Blocks.CaptchaDriver;

/// <summary>
/// <see cref="IHttpClientService"/> implementation backed by <see cref="IHttpClientFactory"/>.
/// </summary>
public sealed class HttpClientService : IHttpClientService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HttpClientService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage? request, string? contentType)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (contentType is null)
        {
            throw new ArgumentNullException(nameof(contentType));
        }

        var client = _httpClientFactory.CreateClient(nameof(CaptchaDriver));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", contentType);
        return await client.SendAsync(request);
    }
}
