namespace Iam.DomainService.SignupLinks;

public class RevokeSignupLinkResponse
{
    public bool IsSuccess { get; set; }
    public string? ItemId { get; set; }
    public Dictionary<string, string>? Errors { get; set; }
}

public class RevokeSignupLinksByConfigurationResponse
{
    public bool IsSuccess { get; set; }
    public long RevokedCount { get; set; }
    public Dictionary<string, string>? Errors { get; set; }
}

/// <summary>
/// Service-level result for generate: carries HTTP status hint for 400 vs 403.
/// </summary>
public class GenerateSignupLinkResult
{
    public bool IsSuccess { get; set; }
    public int StatusCode { get; set; } = 200;
    public Dictionary<string, string>? Errors { get; set; }
    public GenerateSignupLinkResponse? Data { get; set; }

    public static GenerateSignupLinkResult Ok(GenerateSignupLinkResponse data) => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        Data = data
    };

    public static GenerateSignupLinkResult Fail(int statusCode, string key, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Errors = new Dictionary<string, string> { { key, message } }
    };

    public static GenerateSignupLinkResult Fail(int statusCode, Dictionary<string, string> errors) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Errors = errors
    };
}
