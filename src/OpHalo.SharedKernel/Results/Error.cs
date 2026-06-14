namespace OpHalo.SharedKernel.Results;

/// <summary>
/// Represents a structured application or domain error.
/// </summary>
public sealed record Error
{
    private Error(string code, string message)
    {
        Code = code;
        Message = message;
    }

    /// <summary>
    /// Represents the absence of an error.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>
    /// Gets the machine-readable error code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Creates a new error instance.
    /// </summary>
    /// <param name="code">The machine-readable error code.</param>
    /// <param name="message">The human-readable error message.</param>
    public static Error Create(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new Error(code, message);
    }

    /// <summary>
    /// Determines whether this error matches the specified code.
    /// </summary>
    /// <param name="code">The error code to compare.</param>
    public bool Is(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return string.Equals(Code, code, StringComparison.Ordinal);
    }
}
