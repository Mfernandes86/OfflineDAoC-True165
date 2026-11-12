using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q27: a grouped Healer (Amnesia) or Runemaster (Confusion) sheds a monster to the tank.</summary>
    [TestFixture]
    public class UT_VeteranShedAggro
    {
        [Test]
        public void ShedOnlyWithALivingTankToTakeItBack() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.ShedsAggro(true, false, true, true, false), Is.True);
            Assert.That(BotBrain.ShedsAggro(false, false, true, true, false), Is.False, "solo: nobody to take it");
            Assert.That(BotBrain.ShedsAggro(true, true, true, true, false), Is.False, "the tank keeps it");
            Assert.That(BotBrain.ShedsAggro(true, false, false, true, false), Is.False, "no living tank");
            Assert.That(BotBrain.ShedsAggro(true, false, true, false, false), Is.False, "nothing on me");
            Assert.That(BotBrain.ShedsAggro(true, false, true, true, true), Is.False, "RvR keeps its own rules");
        });
    }
}
