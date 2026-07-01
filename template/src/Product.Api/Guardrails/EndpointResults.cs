using Product.Abstractions;

namespace Product.Api.Guardrails;

internal static class EndpointResults
{
    public static IResult Ok<TValue>(Result<TValue> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error);

    public static IResult Problem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        return Results.Problem(title: error.Code, detail: error.Message, statusCode: statusCode);
    }
}
