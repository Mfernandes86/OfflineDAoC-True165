using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q4: two-hander tanks were swapped to a shield they never trained for guard duty. The
    /// swap now needs Shields trained to at least half the bot's level.
    /// </summary>
    [TestFixture]
    public class UT_VeteranGuardShield
    {
        [Test]
        public void GuardDutyNeedsAShieldTrainedToHalfTheLevel() => Assert.Multiple(() =>
        {
            Assert.That(BotWeaponSetPolicy.ShieldTrainedForGuard(1, 40), Is.False, "a two-hander Armsman with Shields 1");
            Assert.That(BotWeaponSetPolicy.ShieldTrainedForGuard(19, 40), Is.False);
            Assert.That(BotWeaponSetPolicy.ShieldTrainedForGuard(20, 40), Is.True);
            Assert.That(BotWeaponSetPolicy.ShieldTrainedForGuard(42, 50), Is.True, "a shield tank");
            Assert.That(BotWeaponSetPolicy.ShieldTrainedForGuard(1, 2), Is.True, "level 2: one point is half");
        });
    }
}
