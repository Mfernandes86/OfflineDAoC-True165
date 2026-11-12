using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Bots drink the potions they carry; rich gamebots keep a heal draught (veteran-bots review M9 / G10, O13).</summary>
[TestFixture]
public class UT_BotPotions165
{
    [Test]
    public void ABagWithNoDraughtIsNotLookedAtOnEveryHit() => Assert.Multiple(() =>
    {
        // found no draught at 1,000: marked until 31,000 for inventory revision 4
        long until = 1_000 + BotPotions165.NoPotionRecheckMilliseconds;
        Assert.That(BotPotions165.ShouldLook(1_200, until, 4, 4), Is.False, "the next hit does not scan the bag again");
        Assert.That(BotPotions165.ShouldLook(until, until, 4, 4), Is.True, "looks again after the recheck time");
        Assert.That(BotPotions165.ShouldLook(1_200, until, 4, 5), Is.True, "the inventory changed (loot, a buy): looks at once");
        Assert.That(BotPotions165.ShouldLook(0, 0, 0, 0), Is.True, "never marked");
    });

    [Test]
    public void TheSpellTypeEnumMapsLikeItsName() => Assert.Multiple(() =>
    {
        foreach (eSpellType type in new[] { eSpellType.Heal, eSpellType.PowerHeal, eSpellType.EnduranceHeal, eSpellType.HealthRegenBuff })
            Assert.That(BotPotions165.KindOf(type), Is.EqualTo(BotPotions165.KindOf(type.ToString())), type.ToString());
    });

    [Test]
    public void OnlyHealPowerAndEnduranceDraughtsAreDrunk()
    {
        Assert.That(BotPotions165.KindOf("Heal"), Is.EqualTo(BotPotions165.Kind.Heal));
        Assert.That(BotPotions165.KindOf("PowerHeal"), Is.EqualTo(BotPotions165.Kind.Power));
        Assert.That(BotPotions165.KindOf("EnduranceHeal"), Is.EqualTo(BotPotions165.Kind.Endurance));
        Assert.That(BotPotions165.KindOf("HealthRegenBuff"), Is.EqualTo(BotPotions165.Kind.None));
    }

    [Test]
    public void APotionIsABackpackMagicalItemWithASpellAndCharges()
    {
        var draught = new DbInventoryItem { Object_Type = (int)eObjectType.Magical, Item_Type = (int)eInventorySlot.FirstBackpack, SpellID = 8053, Charges = 9 };
        Assert.That(BotPotions165.IsPotion(draught), Is.True);
        draught.Charges = 0;
        Assert.That(BotPotions165.IsPotion(draught), Is.False, "used up");
        var charm = new DbInventoryItem { Object_Type = (int)eObjectType.Magical, Item_Type = (int)eInventorySlot.Neck, SpellID = 8053, Charges = 9 };
        Assert.That(BotPotions165.IsPotion(charm), Is.False, "a charged necklace is worn, not drunk");
    }

    [Test]
    public void AHitBotDrinksAHealDraughtAtThirtyFivePercentOnlyWhenNotCastingOrSwinging()
    {
        Assert.That(BotPotions165.WantsHealWhenHit(35, false, false), Is.True);
        Assert.That(BotPotions165.WantsHealWhenHit(36, false, false), Is.False);
        Assert.That(BotPotions165.WantsHealWhenHit(20, true, false), Is.False, "casting");
        Assert.That(BotPotions165.WantsHealWhenHit(20, false, true), Is.False, "a cast potion can't be drunk in attack mode");
        Assert.That(BotPotions165.WantsHealWhenHit(0, false, false), Is.False, "dead");
    }

    [Test]
    public void AfterAKillWithAnAddLeftTheBotDrinksWhatItIsShortOf()
    {
        Assert.That(BotPotions165.WantsAfterKill(40, 90, true, 90, addsLeft: false), Is.EqualTo(BotPotions165.Kind.None), "no add: it rests");
        Assert.That(BotPotions165.WantsAfterKill(40, 10, true, 90, true), Is.EqualTo(BotPotions165.Kind.Heal), "health first");
        Assert.That(BotPotions165.WantsAfterKill(80, 15, true, 90, true), Is.EqualTo(BotPotions165.Kind.Power));
        Assert.That(BotPotions165.WantsAfterKill(80, 0, false, 15, true), Is.EqualTo(BotPotions165.Kind.Endurance));
        Assert.That(BotPotions165.WantsAfterKill(80, 50, true, 10, true), Is.EqualTo(BotPotions165.Kind.None), "a caster drinks no endurance");
    }

    [Test]
    public void OnlyARichGamebotOfLevelTwentyKeepsHealDraughts()
    {
        const long price = 8_000, reserve = 1_600;   // a level-20 bot
        Assert.That(BotPotions165.HealDraughtsWanted(19, 1_000_000, reserve, price), Is.Zero);
        Assert.That(BotPotions165.HealDraughtsWanted(20, price + 5 * reserve - 1, reserve, price), Is.Zero);
        Assert.That(BotPotions165.HealDraughtsWanted(20, price + 5 * reserve, reserve, price), Is.EqualTo(1));
        Assert.That(BotPotions165.HealDraughtsWanted(20, 2 * price + 20 * reserve, reserve, price), Is.EqualTo(2));
    }
}
