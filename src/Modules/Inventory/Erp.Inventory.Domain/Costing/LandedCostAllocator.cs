namespace Erp.Inventory.Domain.Costing;

/// <summary>Baza de repartizare a unui cost adaugat (S3 3.3).</summary>
public enum AllocationBasisKind : byte
{
    Value = 1,
    Quantity = 2,
    Weight = 3,
    Volume = 4,
    Manual = 5,
}

/// <summary>O intrare tinta: valoarea, cantitatea, greutatea si volumul ei (din fisa articolului) si, la repartizarea manuala, suma data.</summary>
public sealed record AllocationTarget(decimal Value, decimal Qty, decimal? WeightKg, decimal? VolumeM3, decimal? ManualAmount);

/// <summary>Repartizare imposibila: baza lipsa (greutate, volum) sau suma manuala diferita de total.</summary>
public sealed class AllocationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Repartizarea unui cost adaugat pe intrarile tinta (02.05, S3 3.3): proportional cu baza aleasa, la 2 zecimale, cu restul de rotunjire pe linia
/// cu baza cea mai mare (suma liniilor = suma documentului); manual, sumele date trebuie sa insumeze totalul.
/// </summary>
public static class LandedCostAllocator
{
    public const string BasisMissing = "INV_ALLOCATION_BASIS_MISSING_DATA";

    public const string ManualMismatch = "INV_LANDED_COST_ALLOCATION_MISMATCH";

    public static IReadOnlyList<decimal> Allocate(decimal amount, AllocationBasisKind basis, IReadOnlyList<AllocationTarget> targets)
    {
        if (targets.Count == 0)
        {
            throw new AllocationException(ManualMismatch, "Documentul de costuri adăugate trebuie să aibă cel puțin o intrare țintă.");
        }

        if (basis == AllocationBasisKind.Manual)
        {
            var manual = targets.Select(t => t.ManualAmount ?? 0).ToList();
            if (manual.Sum() != amount)
            {
                throw new AllocationException(ManualMismatch, $"Sumele manuale ({manual.Sum():0.00}) nu sunt egale cu suma documentului ({amount:0.00}).");
            }

            return manual;
        }

        var weights = targets.Select(t => basis switch
        {
            AllocationBasisKind.Value => Math.Abs(t.Value),
            AllocationBasisKind.Quantity => t.Qty,
            AllocationBasisKind.Weight => t.WeightKg ?? throw new AllocationException(BasisMissing,
                "Articolul unei intrări nu are greutatea completată; repartizarea după greutate nu se poate face."),
            AllocationBasisKind.Volume => t.VolumeM3 ?? throw new AllocationException(BasisMissing,
                "Articolul unei intrări nu are volumul completat; repartizarea după volum nu se poate face."),
            _ => throw new ArgumentOutOfRangeException(nameof(basis)),
        }).ToList();
        var total = weights.Sum();
        if (total <= 0)
        {
            throw new AllocationException(BasisMissing, "Baza de repartizare a intrărilor este zero; alegeți altă bază sau repartizarea manuală.");
        }

        var result = weights.Select(w => decimal.Round(amount * w / total, 2, MidpointRounding.AwayFromZero)).ToList();
        var largest = weights.IndexOf(weights.Max());
        result[largest] += amount - result.Sum();
        return result;
    }
}
