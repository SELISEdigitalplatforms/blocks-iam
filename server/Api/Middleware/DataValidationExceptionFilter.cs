using Blocks.Genesis;
using Blocks.Genesis.DataAccess.Rules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api.Middleware;

/// <summary>
/// A save refused by a system schema's validation rules (IIamDataAccess PrepareWriteAsync / CheckUpdateAsync)
/// is the caller's mistake, not a server error: 400 with one message per property, the field's path as key.
/// </summary>
public sealed class DataValidationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Exception is not DataValidationException exception)
            return;

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var error in exception.Result.Errors)
            errors[error.FieldName] = errors.TryGetValue(error.FieldName, out var earlier) ? $"{earlier}; {error.Message}" : error.Message;

        context.Result = new BadRequestObjectResult(new BaseResponse { IsSuccess = false, Errors = errors });
        context.ExceptionHandled = true;
    }
}
