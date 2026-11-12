using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests
{
    /// <summary>Pure rules behind the realm-event boss script fixes (bosses lane, 2026-11-12).</summary>
    [TestFixture]
    public class UT_BossScriptRules
    {
        [TestCase(false, true, true, true, TestName = "A living pet is devoured")]
        [TestCase(true, true, true, false, TestName = "A gamebot is never devoured although its brain is a controlled brain")]
        [TestCase(false, false, true, false, TestName = "An ordinary monster is not a pet")]
        [TestCase(false, true, false, false, TestName = "A dead pet is not devoured again")]
        public void PrincesDevourPetsOnly(bool isGameBot, bool hasControlledBrain, bool isAlive, bool expected)
        {
            Assert.That(EpicEncounterMechanics.IsDevourablePet(isGameBot, hasControlledBrain, isAlive), Is.EqualTo(expected));
        }
    }
}
