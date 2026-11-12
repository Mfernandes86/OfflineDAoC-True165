using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q32: assassins switch poisons for RvR - crippling off-hand, lethal or disease main hand.</summary>
    [TestFixture]
    public class UT_VeteranRvrPoisons
    {
        [Test]
        public void RvrPutsCripplingOnTheOffHand() => Assert.Multiple(() =>
        {
            Assert.That(BotPoisonSupply.OffHandPoisons(true)[0], Is.EqualTo(eSpellType.UnbreakableSpeedDecrease));
            Assert.That(BotPoisonSupply.MainHandPoisons(true), Does.Contain(eSpellType.Disease));
            Assert.That(BotPoisonSupply.StockedPoisons(true), Does.Contain(eSpellType.UnbreakableSpeedDecrease));
        });

        [Test]
        public void PveIsUnchanged() => Assert.Multiple(() =>
        {
            Assert.That(BotPoisonSupply.MainHandPoisons(false), Is.EqualTo(new[] { eSpellType.DamageOverTime }));
            Assert.That(BotPoisonSupply.OffHandPoisons(false),
                Is.EqualTo(new[] { eSpellType.StrengthConstitutionDebuff, eSpellType.DamageOverTime }));
            Assert.That(BotPoisonSupply.StockedPoisons(false),
                Is.EqualTo(new[] { eSpellType.DamageOverTime, eSpellType.StrengthConstitutionDebuff }));
        });
    }
}
