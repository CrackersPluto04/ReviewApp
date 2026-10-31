using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;

namespace ReviewApp.Api.Tests.Unit;

// The seed is applied by migrations with stable IDs, so a careless edit would corrupt user progress
public class AchievementSeedTests
{
    private static readonly IReadOnlyList<Achievement> Seed = AchievementSeed.Achievements;

    [Fact]
    public void Seed_HasSevenGroupsOfThreeTiers()
    {
        Seed.Count.ShouldBe(21);
        Seed.GroupBy(a => a.GroupCode).ShouldAllBe(g => g.Count() == 3);
    }

    [Fact]
    public void Ids_AreContiguousAndStable()
    {
        Seed.Select(a => a.ID).ShouldBe(Enumerable.Range(1, 21));
    }

    [Fact]
    public void Codes_AreUniqueAndDerivedFromGroupAndTier()
    {
        Seed.Select(a => a.Code).Distinct().Count().ShouldBe(Seed.Count);
        Seed.ShouldAllBe(a => a.Code == $"{a.GroupCode}_{a.Tier.ToString().ToUpperInvariant()}");
    }

    [Fact]
    public void EachGroup_HasBronzeSilverGoldWithRisingTargets()
    {
        foreach (var group in Seed.GroupBy(a => a.GroupCode))
        {
            var tiers = group.OrderBy(a => a.ID).ToList();
            tiers.Select(a => a.Tier).ShouldBe([AchievementTier.Bronze, AchievementTier.Silver, AchievementTier.Gold]);
            tiers[0].TargetValue.ShouldBeGreaterThan(0);
            tiers[1].TargetValue.ShouldBeGreaterThan(tiers[0].TargetValue);
            tiers[2].TargetValue.ShouldBeGreaterThan(tiers[1].TargetValue);
        }
    }

    [Fact]
    public void EachGroup_SharesOneMetricCategoriesAndIcon_AndEveryMetricHasAGroup()
    {
        var groups = Seed.GroupBy(a => a.GroupCode).ToList();

        groups.ShouldAllBe(g => g.Select(a => a.Metric).Distinct().Count() == 1);
        groups.ShouldAllBe(g => g.Select(a => a.Categories).Distinct().Count() == 1);
        groups.ShouldAllBe(g => g.Select(a => a.IconKey).Distinct().Count() == 1);
        groups.Select(g => g.First().Metric).OrderBy(m => m).ShouldBe(Enum.GetValues<AchievementMetric>());
    }

    [Fact]
    public void Fields_FitTheColumnLimits()
    {
        Seed.ShouldAllBe(a => a.Code.Length <= 50 && a.GroupCode.Length <= 50 && a.Title.Length <= 50
            && a.Description.Length <= 100 && a.IconKey.Length <= 50);
        Seed.ShouldAllBe(a => a.Categories != AchievementCategories.None);
    }
}
