using BankAccounting.BuildingBlocks;

namespace BankAccounting.Host.Endpoints;

public static class ApiResults
{
    /// <summary>RFC 7807 ProblemDetails with a stable machine-readable "code" extension.</summary>
    public static IResult Problem(Error error) => Results.Problem(
        title: error.Code,
        detail: error.Message,
        statusCode: error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unprocessable => StatusCodes.Status422UnprocessableEntity,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        },
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    public static Guid? UserId(this HttpContext http) =>
        Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;
}
