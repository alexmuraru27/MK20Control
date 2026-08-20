using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Mk20Control.Protocol.Compat;

/// <summary>
/// Argument validation matching the .NET 7+ <c>ThrowIfXxx</c> helpers, which do not
/// exist on .NET Framework. Behaviour and exception types are identical.
/// </summary>
internal static class Guard
{
    public static void NotNull(
        [NotNull] object? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    public static void NotNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The value cannot be an empty string or composed entirely of whitespace.",
                paramName);
        }
    }

    public static void NotNegative(
        int value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} ('{value}') must be a non-negative value.");
        }
    }

    public static void LessThan(
        int value,
        int other,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value >= other)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} ('{value}') must be less than '{other}'.");
        }
    }

    public static void NotDisposed(bool condition, object instance)
    {
        if (condition)
        {
            throw new ObjectDisposedException(instance?.GetType().FullName);
        }
    }
}
