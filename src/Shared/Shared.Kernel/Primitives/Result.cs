namespace Shared.Kernel.Primitives;

public sealed class Result
{
    private Result(bool isSuccess, Shared.Kernel.Errors.Error error)
    {
        if (isSuccess && error != Shared.Kernel.Errors.Error.None)
        {
            throw new InvalidOperationException("Success result cannot have an error.");
        }
        if (!isSuccess && error == Shared.Kernel.Errors.Error.None)
        {
            throw new InvalidOperationException("Failure result must have an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Shared.Kernel.Errors.Error Error { get; }

    public static Result Success() => new(true, Shared.Kernel.Errors.Error.None);
    public static Result Failure(Shared.Kernel.Errors.Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(Shared.Kernel.Errors.Error error) => Result<T>.Failure(error);
}

public sealed class Result<T>
{
    private Result(T? value, Shared.Kernel.Errors.Error error, bool isSuccess)
    {
        Value = value;
        Error = error;
        IsSuccess = isSuccess;
    }

    public T? Value { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Shared.Kernel.Errors.Error Error { get; }

    public static Result<T> Success(T value) => new(value, Shared.Kernel.Errors.Error.None, true);
    public static Result<T> Failure(Shared.Kernel.Errors.Error error) => new(default, error, false);
}
