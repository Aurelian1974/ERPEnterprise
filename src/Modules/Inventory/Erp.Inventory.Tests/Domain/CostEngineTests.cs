using Erp.Inventory.Domain.Costing;

namespace Erp.Inventory.Tests.Domain;

/// <summary>Motorul de cost (02.05, S3 3.1–3.4): FIFO, LIFO, CMP, straturile negative, transferul, stornarea si regulile valorii.</summary>
public sealed class CostEngineTests
{
    private static readonly DateOnly Day = new(2026, 11, 2);
    private static long seq;

    private static CostLineResult Post(CostKeyState key, decimal qty, decimal? unit = null, int day = 0) =>
        CostEngine.Apply(key, new CostLine((int)Interlocked.Increment(ref seq), qty, Day.AddDays(day), seq, unit));

    [Theory]
    [Trait("Criteriu", "02.05-S3-A3")]
    [InlineData(CostMethod.Fifo, 1600)]
    [InlineData(CostMethod.Lifo, 1700)]
    [InlineData(CostMethod.Average, 1650)]
    public void Issue_cost_follows_the_method(CostMethod method, decimal expected)
    {
        var key = new CostKeyState { Method = method };
        Post(key, 100, 10);
        Post(key, 100, 12, 1);
        var issue = Post(key, -150, day: 2);
        Assert.Equal(-expected, issue.Amount);
        Assert.Equal(2200 - expected, key.Value);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-A7")]
    public void A_negative_issue_at_the_last_cost_is_regularised_exactly_by_the_next_receipt()
    {
        foreach (var method in new[] { CostMethod.Fifo, CostMethod.Average })
        {
            var key = new CostKeyState { Method = method };
            Post(key, 10, 5);
            Post(key, -10);
            var negative = Post(key, -4);
            Assert.Equal((-20m, true), (negative.Amount, negative.WentNegative));
            var receipt = Post(key, 10, 6.5m);
            var adjustment = Assert.Single(receipt.Adjustments);
            // 4 bucati iesite la 5,00 costau de fapt 6,50: regularizarea pe iesire e -6,00, iar stocul ramas (6 buc) valoreaza 39,00.
            Assert.Equal((-6m, negative.Row), (adjustment.Amount, adjustment.MovementRow!.Value));
            Assert.Equal((6m, 39m), (key.Qty, key.Value));
        }
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-A2")]
    [Trait("Criteriu", "02.05-S3-A1")]
    public void Random_sequences_keep_value_equal_to_movements_plus_adjustments_and_zero_quantity_means_zero_value()
    {
        var random = new Random(26112026);
        foreach (var method in new[] { CostMethod.Fifo, CostMethod.Lifo, CostMethod.Average })
        {
            var key = new CostKeyState { Method = method, FallbackUnitCost = 3 };
            decimal movements = 0, adjustments = 0;
            for (var i = 0; i < 400; i++)
            {
                var result = random.Next(3) == 0 || key.Qty <= 0
                    ? Post(key, random.Next(1, 50), random.Next(100, 999) / 37m, i)
                    : Post(key, -random.Next(1, (int)Math.Max(1, key.Qty + 5)), day: i);
                movements += result.Amount;
                adjustments += result.Adjustments.Sum(a => a.Amount);
                Assert.Equal(movements + adjustments, key.Value);
                if (key.Qty == 0)
                {
                    Assert.Equal(0m, key.Value);
                }

                if (method != CostMethod.Average)
                {
                    Assert.Equal(key.Qty, key.Layers.Sum(l => l.QtyRemaining));
                    Assert.Equal(key.Value, key.Layers.Sum(l => l.ValueRemaining));
                }
            }
        }
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-A4")]
    public void A_transfer_moves_the_layers_with_their_original_dates_and_keeps_the_value()
    {
        var source = new CostKeyState { Method = CostMethod.Fifo };
        var target = new CostKeyState { Method = CostMethod.Fifo };
        Post(source, 3, 10);
        Post(source, 5, 11, 3);
        var issue = Post(source, -6, day: 5);
        var counter = new CostCounter(-issue.Amount, issue.Consumptions.Select(c => new CostCounterPart(c.Qty, c.Value, c.Layer.LayerDate, c.Layer.Seq, c.Layer.Id)).ToList());
        var receipt = CostEngine.Apply(target, new CostLine(999, 6, Day.AddDays(5), 999, null, counter));

        Assert.Equal(-issue.Amount, receipt.Amount);
        Assert.Equal([Day, Day.AddDays(3)], receipt.CreatedLayers.Select(l => l.LayerDate));
        Assert.Equal(85m, source.Value + target.Value);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-3.2")]
    public void Reversing_an_issue_puts_the_quantities_back_into_their_layers_and_a_consumed_receipt_cannot_be_reversed()
    {
        var key = new CostKeyState { Method = CostMethod.Fifo };
        var receipt = Post(key, 4, 2.5m);
        Post(key, 4, 3, 1);
        var issue = Post(key, -6, day: 2);
        CostEngine.Apply(key, new CostLine(500, 6, Day.AddDays(3), 500, null, null,
            new CostReversal(issue.Amount, issue.Consumptions.Select(c => (c.Layer, c.Qty, c.Value)).ToList(), (CostLayerState?)null)));
        Assert.Equal((8m, 22m), (key.Qty, key.Value));
        Assert.Equal([4m, 4m], key.Layers.Select(l => l.QtyRemaining));

        Post(key, -5, day: 4);
        Assert.Throws<CostLayerConsumedException>(() => CostEngine.Apply(key, new CostLine(501, -4, Day.AddDays(5), 501, null, null,
            new CostReversal(receipt.Amount, [], receipt.CreatedLayers.Single()))));
    }
}

/// <summary>Schimbarea metodei de evaluare (02.05, S1 4.5): valoarea cheii ramane aceeasi, la ban.</summary>
public sealed class CostMethodChangeTests
{
    [Fact]
    [Trait("Criteriu", "02.05-S1-A11")]
    public void Changing_the_method_keeps_the_value_of_every_key()
    {
        var random = new Random(4511);
        var day = new DateOnly(2026, 11, 2);
        for (var run = 0; run < 300; run++)
        {
            var from = (CostMethod)random.Next(1, 4);
            var to = (CostMethod)random.Next(1, 4);
            var key = new CostKeyState { Method = from };
            var row = 0;
            for (var i = 0; i < random.Next(1, 12); i++)
            {
                row++;
                var qty = random.Next(3) == 0 && key.Qty > 0 ? -random.Next(1, (int)key.Qty + 1) : random.Next(1, 40);
                CostEngine.Apply(key, new CostLine(row, qty, day.AddDays(i), row, qty > 0 ? random.Next(100, 999) / 37m : null));
            }

            var before = key.Value;
            CostEngine.ChangeMethod(key, to, day.AddDays(20), row, row);
            Assert.Equal(before, key.Value);
            if (to != CostMethod.Average)
            {
                Assert.Equal(key.Value, key.Layers.Sum(l => l.ValueRemaining));
                Assert.Equal(key.Qty, key.Layers.Sum(l => l.QtyRemaining));
            }

            // O iesire care goleste cheia ia toata valoarea, cu noua metoda.
            if (key.Qty > 0)
            {
                var issue = CostEngine.Apply(key, new CostLine(row + 1, -key.Qty, day.AddDays(21), row + 1, null));
                Assert.Equal(-before, issue.Amount);
                Assert.Equal(0m, key.Value);
            }
        }
    }
}
