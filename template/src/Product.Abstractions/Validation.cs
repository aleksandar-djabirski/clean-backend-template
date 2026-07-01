namespace Product.Abstractions;

// Validators are synchronous by design: validation is pure, in-memory request shape checking.
// Anything that needs I/O is a business rule for a command/query handler, not a validator.
public interface IValidator<in TRequest>
{
    IReadOnlyList<Error> Validate(TRequest request);
}

public static class ValidationResult
{
    public static IReadOnlyList<Error> Valid { get; } = Array.Empty<Error>();
}
