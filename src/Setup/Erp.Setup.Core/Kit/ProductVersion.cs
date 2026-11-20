using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Erp.Setup.Core.Kit;

/// <summary>
/// Versiunea produsului AAAA.LL.Major.Release.ServicePack (GIT-05, GIT-06). System.Version are cel mult 4 componente,
/// deci comparatia se face aici; o forma mai scurta (de exemplu 2026.4.2) se completeaza cu zerouri.
/// </summary>
public readonly struct ProductVersion : IComparable<ProductVersion>, IEquatable<ProductVersion>
{
    private const int MaxParts = 5;
    private readonly int[]? parts;

    private ProductVersion(int[] parts) => this.parts = parts;

    public static bool TryParse([NotNullWhen(true)] string? text, out ProductVersion version)
    {
        version = default;
        var pieces = text?.Split('.');
        if (pieces is null || pieces.Length is < 1 or > MaxParts)
        {
            return false;
        }

        var numbers = new int[MaxParts];
        for (var i = 0; i < pieces.Length; i++)
        {
            if (!int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new ProductVersion(numbers);
        return true;
    }

    public static ProductVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Versiunea „{text}” nu are forma AAAA.LL.Major.Release.ServicePack.");

    public int CompareTo(ProductVersion other)
    {
        var left = parts ?? new int[MaxParts];
        var right = other.parts ?? new int[MaxParts];
        for (var i = 0; i < MaxParts; i++)
        {
            var result = left[i].CompareTo(right[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    public bool Equals(ProductVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ProductVersion other && Equals(other);

    public override int GetHashCode() => (parts ?? new int[MaxParts]).Aggregate(17, (hash, part) => hash * 31 + part);

    public override string ToString() => string.Join('.', (parts ?? new int[MaxParts]).Select(p => p.ToString(CultureInfo.InvariantCulture)));

    public static bool operator <(ProductVersion left, ProductVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ProductVersion left, ProductVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ProductVersion left, ProductVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ProductVersion left, ProductVersion right) => left.CompareTo(right) >= 0;

    public static bool operator ==(ProductVersion left, ProductVersion right) => left.Equals(right);

    public static bool operator !=(ProductVersion left, ProductVersion right) => !left.Equals(right);
}
