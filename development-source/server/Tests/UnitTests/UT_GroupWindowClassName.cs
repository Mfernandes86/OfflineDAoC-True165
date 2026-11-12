using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>GitHub #49: the group window names each member's class as a player of that class and gender sees it.</summary>
[TestFixture]
public sealed class UT_GroupWindowClassName
{
    [TestCase("Armsman", "Armswoman", true, "Armswoman")]
    [TestCase("Armsman", "Armswoman", false, "Armsman")]
    [TestCase("Enchanter", "Enchantress", true, "Enchantress")]
    [TestCase("Enchanter", "", true, "Enchanter")]
    [TestCase("Druid", null, true, "Druid")]
    [TestCase("Druid", null, false, "Druid")]
    public void FemaleTitleOnlyForAFemaleMemberWhoseClassHasOne(string name, string femaleName, bool female, string expected) =>
        Assert.That(PacketLib1125.GroupWindowClassName(name, femaleName, female), Is.EqualTo(expected));
}
