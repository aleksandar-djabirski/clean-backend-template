namespace Product.Abstractions;

public interface IValidator<in TRequest>
{
    ValueTask<IReadOnlyList<Error>> ValidateAsync(TRequest request, CancellationToken cancellationToken);
}

public static class ValidationResult
{
    public static IReadOnlyList<Error> Valid { get; } = Array.Empty<Error>();
}
