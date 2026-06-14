namespace OpHalo.SharedKernel.Results;

/// <summary>
/// Represents the outcome of an operation without a return value.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot contain an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must contain an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>
    /// Gets a value indicating whether the operation completed successfully.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error associated with a failed result.
    /// </summary>
    public Error Error { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>
    /// Executes one of two functions based on whether the result succeeded or failed.
    /// </summary>
    /// <typeparam name="TResult">The return type of the functions.</typeparam>
    /// <param name="onSuccess">Function to execute if the result succeeded.</param>
    /// <param name="onFailure">Function to execute if the result failed.</param>
    public TResult Match<TResult>(
        Func<TResult> onSuccess,
        Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    /// <summary>
    /// Executes one of two actions based on whether the result succeeded or failed.
    /// </summary>
    /// <param name="onSuccess">Action to execute if the result succeeded.</param>
    /// <param name="onFailure">Action to execute if the result failed.</param>
    public void Match(
        Action onSuccess,
        Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (IsSuccess)
        {
            onSuccess();
            return;
        }

        onFailure(Error);
    }
}

/// <summary>
/// Represents the outcome of an operation with a return value.
/// </summary>
/// <typeparam name="T">The type of the return value.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value)
        : base(true, Error.None)
    {
        _value = value;
    }

    private Result(Error error)
        : base(false, error)
    {
        _value = default;
    }

    /// <summary>
    /// Gets the value of a successful result.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed on a failed result.
    /// </exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result does not contain a value.");

    /// <summary>
    /// Creates a successful result with a value.
    /// </summary>
    /// <param name="value">The value produced by the operation.</param>
    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new Result<T>(value);
    }

    /// <summary>
    /// Creates a failed result with an error.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    public static new Result<T> Failure(Error error) => new(error);

    /// <summary>
    /// Transforms the value of a successful result, or returns a failure unchanged.
    /// </summary>
    /// <typeparam name="TNext">The type of the transformed value.</typeparam>
    /// <param name="map">Function to transform the value.</param>
    public Result<TNext> Map<TNext>(Func<T, TNext> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return IsSuccess
            ? Result<TNext>.Success(map(_value!))
            : Result<TNext>.Failure(Error);
    }

    /// <summary>
    /// Chains multiple operations, short-circuiting on the first failure.
    /// </summary>
    /// <typeparam name="TNext">The type of the next operation's result value.</typeparam>
    /// <param name="bind">Function returning the next operation's result.</param>
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);

        return IsSuccess
            ? bind(_value!)
            : Result<TNext>.Failure(Error);
    }

    /// <summary>
    /// Executes one of two functions based on whether the result succeeded or failed.
    /// </summary>
    /// <typeparam name="TResult">The return type of the functions.</typeparam>
    /// <param name="onSuccess">Function to execute if the result succeeded, receiving the value.</param>
    /// <param name="onFailure">Function to execute if the result failed, receiving the error.</param>
    public TResult Match<TResult>(
        Func<T, TResult> onSuccess,
        Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }

    /// <summary>
    /// Executes one of two actions based on whether the result succeeded or failed.
    /// </summary>
    /// <param name="onSuccess">Action to execute if the result succeeded, receiving the value.</param>
    /// <param name="onFailure">Action to execute if the result failed, receiving the error.</param>
    public void Match(
        Action<T> onSuccess,
        Action<Error> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (IsSuccess)
        {
            onSuccess(_value!);
            return;
        }

        onFailure(Error);
    }

    /// <summary>
    /// Attempts to get the value, returning false if the result is a failure.
    /// </summary>
    /// <param name="value">The output value when the result succeeds.</param>
    public bool TryGetValue(out T? value)
    {
        if (IsSuccess)
        {
            value = _value;
            return true;
        }

        value = default;
        return false;
    }
}
