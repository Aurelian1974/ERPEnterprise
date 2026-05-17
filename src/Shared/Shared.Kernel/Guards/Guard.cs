using Shared.Kernel.Errors;

namespace Shared.Kernel.Guards;

public static class Guard
{
    public static T AgainstNull<T>(T? value, Error error)
    {
        if (value is null)
        {
            throw new ArgumentNullException(error.Code, error.Description);
        }
        return value;
    }

    public static string AgainstNullOrWhiteSpace(string? value, Error error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(error.Description, error.Code);
        }
        return value;
    }

    public static T AgainstDefault<T>(T value, Error error) where T : struct
    {
        if (value.Equals(default(T)))
        {
            throw new ArgumentException(error.Description, error.Code);
        }
        return value;
    }

    public static int AgainstNegativeOrZero(int value, Error error)
    {
        if (value <= 0)
        {
            throw new ArgumentException(error.Description, error.Code);
        }
        return value;
    }

    public static decimal AgainstNegative(decimal value, Error error)
    {
        if (value < 0)
        {
            throw new ArgumentException(error.Description, error.Code);
        }
        return value;
    }
}
