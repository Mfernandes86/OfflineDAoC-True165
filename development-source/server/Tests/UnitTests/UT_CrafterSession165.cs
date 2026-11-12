using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Crafter gamebots stop burning coin (veteran-bots review M9 / economy G2).</summary>
[TestFixture]
public class UT_CrafterSession165
{
    private static CrafterBots165.Recipe[] Recipes(params int[] levels) =>
        levels.Select(l => new CrafterBots165.Recipe($"r{l}", l, 100)).ToArray();

    [Test]
    public void TheRecipeIsRandomInTheBandWeightedTowardTheTop()
    {
        var all = Recipes(10, 40, 47, 50, 55, 60, 61, 80);
        var random = new Random(7);
        var counts = new Dictionary<int, int>();
        for (int i = 0; i < 5_000; i++)
        {
            int level = CrafterBots165.PickRecipe(all, 50, random).Level;
            counts[level] = counts.GetValueOrDefault(level) + 1;
        }
        Assert.That(counts.Keys, Is.EquivalentTo(new[] { 47, 50, 55, 60 }), "only recipes in [skill - 3, skill + 10]");
        Assert.That(counts[60], Is.GreaterThan(counts[55]));
        Assert.That(counts[55], Is.GreaterThan(counts[50]));
        Assert.That(counts[50], Is.GreaterThan(counts[47]));
    }

    [Test]
    public void ANewCrafterStillStartsOnTheLinesEasiestRecipe()
    {
        Assert.That(CrafterBots165.PickRecipe(Recipes(15, 20, 30), 1, new Random(1)).Level, Is.EqualTo(15));
        Assert.That(CrafterBots165.PickRecipe(Recipes(1, 2, 3), 400, new Random(1)).Level, Is.EqualTo(3),
            "past every recipe: the hardest one");
        Assert.That(CrafterBots165.PickRecipe(Array.Empty<CrafterBots165.Recipe>(), 10, new Random(1)), Is.Null);
    }

    [Test]
    public void OnlyTheBestProductIsListedMasterpieceAndGearFirst()
    {
        Assert.That(CrafterBots165.BestProductIndex(new List<(bool, int)> { (false, 96), (false, 100), (false, 99) }), Is.EqualTo(1));
        Assert.That(CrafterBots165.BestProductIndex(new List<(bool, int)> { (true, 100), (false, 94) }), Is.EqualTo(1));
        Assert.That(CrafterBots165.BestProductIndex(new List<(bool, int)> { (true, 100) }), Is.Zero);
        Assert.That(CrafterBots165.BestProductIndex(new List<(bool, int)>()), Is.EqualTo(-1));
    }

    [Test]
    public void AnUnlistedProductBringsTheBetterOfSaleAndSalvageNeverLessThanHalfItsMaterials()
    {
        Assert.That(CrafterBots165.DisposalValue(1_000, 300, 1_000, false), Is.EqualTo(500), "today's floor");
        Assert.That(CrafterBots165.DisposalValue(1_000, 800, 1_000, false), Is.EqualTo(800), "the merchant pays more");
        Assert.That(CrafterBots165.DisposalValue(100, 10, 4_000, false), Is.EqualTo(900), "salvage pays more");
        Assert.That(CrafterBots165.DisposalValue(100, 10, 4_000, true), Is.EqualTo(50), "potions can't be salvaged");
    }

    [Test]
    public void AlchemistsMakeThreeCombinesASession()
    {
        Assert.That(CrafterBots165.CombinesPerSession(eCraftingSkill.Alchemy), Is.EqualTo(3));
        Assert.That(CrafterBots165.CombinesPerSession(eCraftingSkill.ArmorCrafting), Is.EqualTo(10));
    }
}
