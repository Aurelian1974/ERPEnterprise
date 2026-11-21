using Erp.Core.Application.Dimensions;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Dimensions;

/// <summary>Combinarea regulilor implicite si validarea dimensiunilor (02.01, 3.6 punctul 14).</summary>
public sealed class DimensionRuleEngineTests
{
    private static DefaultDimensionRule Rule(DimensionSource source, string type, int? value, DimensionPosting posting) => new(source, type, value, posting);

    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public void Item_value_wins_over_partner_and_location_and_the_strictest_posting_applies()
    {
        var merged = DimensionRuleEngine.Merge([
            Rule(DimensionSource.Location, "COST_CENTER", 3, DimensionPosting.Optional),
            Rule(DimensionSource.Partner, "COST_CENTER", 2, DimensionPosting.Mandatory),
            Rule(DimensionSource.Item, "COST_CENTER", 1, DimensionPosting.Optional),
            Rule(DimensionSource.Partner, "PROJECT", null, DimensionPosting.Mandatory),
        ]);
        Assert.Equal(
            [new DimensionDefault("COST_CENTER", 1, DimensionPosting.Mandatory, DimensionSource.Item), new DimensionDefault("PROJECT", null, DimensionPosting.Mandatory, DimensionSource.Partner)],
            merged);
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public void Fixed_value_beats_an_optional_value_from_a_stronger_source()
    {
        var merged = DimensionRuleEngine.Merge([
            Rule(DimensionSource.Item, "PROJECT", 5, DimensionPosting.Optional),
            Rule(DimensionSource.Location, "PROJECT", 9, DimensionPosting.Fixed),
        ]);
        Assert.Equal((9, DimensionPosting.Fixed), (merged[0].ValueId, merged[0].Posting));
    }

    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public void Validation_reports_missing_fixed_conflicting_and_blocked_values()
    {
        var rules = new[]
        {
            Rule(DimensionSource.Partner, "PROJECT", null, DimensionPosting.Mandatory),
            Rule(DimensionSource.Item, "COST_CENTER", 1, DimensionPosting.Fixed),
            Rule(DimensionSource.Item, "SITE", 7, DimensionPosting.Fixed),
            Rule(DimensionSource.Location, "SITE", 8, DimensionPosting.Fixed),
        };
        var combinations = new[] { new BlockedCombination("COST_CENTER", 2, "WAREHOUSE", 4), new BlockedCombination("PROJECT", null, "REGIUNE", null) };

        var issues = DimensionRuleEngine.Validate(new Dictionary<string, int?> { ["COST_CENTER"] = 2, ["WAREHOUSE"] = 4 }, rules, combinations);
        Assert.Equal(["CORE_DIMENSION_FIXED", "CORE_DIMENSION_CONFLICT", "CORE_DIMENSION_REQUIRED", "CORE_DIMENSION_COMBINATION_BLOCKED"],
            issues.Select(i => i.Code).Order().Reverse().OrderBy(c => c switch
            {
                "CORE_DIMENSION_FIXED" => 0, "CORE_DIMENSION_CONFLICT" => 1, "CORE_DIMENSION_REQUIRED" => 2, _ => 3,
            }));

        var ok = DimensionRuleEngine.Validate(new Dictionary<string, int?> { ["COST_CENTER"] = 1, ["PROJECT"] = 3 },
            rules.Take(2), [new BlockedCombination("PROJECT", null, "REGIUNE", null)]);
        Assert.Empty(ok);
        var typesTogether = DimensionRuleEngine.Validate(new Dictionary<string, int?> { ["PROJECT"] = 3, ["REGIUNE"] = 1 }, [], combinations);
        Assert.Equal("CORE_DIMENSION_COMBINATION_BLOCKED", Assert.Single(typesTogether).Code);
    }
}
