using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests.Unit.Gameserver
{
    /// <summary>
    /// Night, day and hour-window spawns keep the aggression of their Mob row or NPC template (owner 2026-11-11): their
    /// own brain replaced the loaded one and came up with aggro 0.
    /// </summary>
    [TestFixture]
    public class UT_NightSpawnAggro
    {
        [Test]
        public void TheSpawnsOwnBrainTakesOverTheLoadedAggro()
        {
            var loaded = new StandardMobBrain { AggroLevel = 200, AggroRange = 500 };
            var own = new NightSpawnBrain();
            Assert.That(own.AggroLevel, Is.EqualTo(0), "a fresh brain is passive");

            TimeDependentSpawnNpc.CarryAggro(loaded, own);

            Assert.That(own.AggroLevel, Is.EqualTo(200));
            Assert.That(own.AggroRange, Is.EqualTo(500));
        }

        [Test]
        public void ARespawnKeepsIt()
        {
            // AddToWorld runs again on every respawn: the old brain is then the spawn's own brain
            var first = new DaySpawnBrain { AggroLevel = 90, AggroRange = 650 };
            var second = new DaySpawnBrain();
            TimeDependentSpawnNpc.CarryAggro(first, second);
            Assert.That((second.AggroLevel, second.AggroRange), Is.EqualTo((90, 650)));
        }

        [Test]
        public void APassiveRowStaysPassiveAndAMissingBrainChangesNothing()
        {
            var own = new HourWindowSpawnBrain { AggroLevel = 0, AggroRange = 0 };
            TimeDependentSpawnNpc.CarryAggro(new StandardMobBrain { AggroLevel = 0, AggroRange = 0 }, own);
            Assert.That((own.AggroLevel, own.AggroRange), Is.EqualTo((0, 0)));

            own.AggroLevel = 30;
            TimeDependentSpawnNpc.CarryAggro(null, own);
            Assert.That(own.AggroLevel, Is.EqualTo(30));
            Assert.DoesNotThrow(() => TimeDependentSpawnNpc.CarryAggro(own, null));
        }
    }
}
