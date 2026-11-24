using Erp.Finance.Domain.Accounts;

namespace Erp.Finance.Tests.Domain;

/// <summary>Maparea pe planurile alternative (02.02, C1 3.5, 4.5): impartirea procentuala, sursele si regulile care nu se aplica niciodata.</summary>
public sealed class MappingRulesTests
{
    [Fact]
    [Trait("Criteriu", "02.02-C1-A6")]
    public void A_percentage_split_rounds_each_part_and_the_last_target_takes_the_rest()
    {
        Assert.Equal([600.01m, 400.00m], MappingRules.Split(1_000.01m, [60m, 40m]));
        Assert.Equal([333.33m, 333.33m, 333.34m], MappingRules.Split(1_000m, [33.333333m, 33.333333m, 33.333334m]));
        Assert.Equal([-600.01m, -400.00m], MappingRules.Split(-1_000.01m, [60m, 40m]));
    }

    [Fact]
    public void A_source_account_covers_its_lower_grade_synthetics_and_analytics_and_ranges_use_the_normalized_code()
    {
        Assert.True(MappingRules.Covers("512", "5121.01"));
        Assert.True(MappingRules.Covers("5121", "5121.01"));
        Assert.False(MappingRules.Covers("5121.1", "5121.10"));
        Assert.False(MappingRules.Covers("62", "6"));
        Assert.Equal(("6280", "5121", "6000"), (MappingRules.Normalize("628"), MappingRules.Normalize("5121.01"), MappingRules.Normalize("6")));
    }

    [Fact]
    public void A_rule_covered_by_a_higher_priority_rule_never_applies_while_a_narrower_one_above_only_overlaps()
    {
        var day = new DateOnly(2026, 1, 1);
        MappingRuleShape Rule(int id, int priority, string[] sources, string? dimension = null, byte condition = 0, int? value = null) =>
            new(id, priority, sources.ToHashSet(), null, 0, null, null, null, dimension, condition, value, null, null, day, null);

        var logistics = Rule(60, 60, ["628"], "COST_CENTER", 1, 7);
        var rest = Rule(69, 69, ["622", "624", "628"]);
        var transport = Rule(72, 72, ["624"]);
        var overlaps = MappingRules.Overlaps([transport, logistics, rest]);

        Assert.Contains(overlaps, o => o is { WinnerId: 69, LoserId: 72, IsDead: true });
        Assert.Contains(overlaps, o => o is { WinnerId: 60, LoserId: 69, IsDead: false });
        Assert.DoesNotContain(overlaps, o => o.WinnerId == 60 && o.LoserId == 72);
    }
}
