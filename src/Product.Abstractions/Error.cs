namespace Product.Abstractions;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict
}

public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    public static Error None { get; } = new(string.Empty, string.Empty);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
}
