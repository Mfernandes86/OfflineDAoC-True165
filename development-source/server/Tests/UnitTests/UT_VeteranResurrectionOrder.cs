using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q21: raise the raisers first, then power users, then tanks.</summary>
    [TestFixture]
    public class UT_VeteranResurrectionOrder
    {
        [Test]
        public void HumanThenRaisersThenCastersThenTanksThenTheRest() => Assert.Multiple(() =>
        {
            int human = BotGroupSupport.ResurrectionOrder(true, false, false, true);
            int raiser = BotGroupSupport.ResurrectionOrder(false, true, true, false);
            int caster = BotGroupSupport.ResurrectionOrder(false, false, true, false);
            int tank = BotGroupSupport.ResurrectionOrder(false, false, false, true);
            int melee = BotGroupSupport.ResurrectionOrder(false, false, false, false);
            Assert.That(human, Is.LessThan(raiser), "the human player keeps first place");
            Assert.That(raiser, Is.LessThan(caster));
            Assert.That(caster, Is.LessThan(tank));
            Assert.That(tank, Is.LessThan(melee));
            Assert.That(BotGroupSupport.ResurrectionOrder(false, true, false, true), Is.EqualTo(raiser),
                "a raising Paladin-type counts as a raiser before a tank");
        });
    }
}
