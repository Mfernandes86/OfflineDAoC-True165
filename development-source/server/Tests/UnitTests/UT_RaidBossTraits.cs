using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_RaidBossTraits
    {
        [TestCase(1775, 1400, 1300)]   // a bolt caster at Legion
        [TestCase(1660, 1400, 1300)]   // an archer at Legion
        [TestCase(1500, 1400, 1300)]   // a 1,500 nuke
        [TestCase(1000, 1400, 1000)]   // an Animist's shrooms already stand closer
        [TestCase(1775, 0, 1775)]      // no limit
        [TestCase(0, 1400, 0)]         // no ranged attack
        public void StandOffNeverBeyondTheDamageRange(int own, int limit, int expected) =>
            Assert.That(RaidBossTraits.CapStandOff(own, limit), Is.EqualTo(expected));

        [TestCase(1400, 1340, false)]
        [TestCase(1400, 1360, true)]
        [TestCase(1400, 1775, true)]
        [TestCase(0, 5000, false)]
        public void BeyondDamageRangeStopsTheAttackSpells(int limit, double distance, bool beyond) =>
            Assert.That(RaidBossTraits.BeyondDamageRange(limit, distance), Is.EqualTo(beyond));

        [Test]
        public void ACappedBotStopsCastingOnlyFartherOutThanItWalksTo()
        {
            // the bot walks to the cap less 100 and keeps casting until it is past the cap less 50: no walk/cast flapping
            int cap = RaidBossTraits.CapStandOff(1775, 1400);
            Assert.That(RaidBossTraits.BeyondDamageRange(1400, cap), Is.False);
            Assert.That(RaidBossTraits.BeyondDamageRange(1400, cap - 100), Is.False);
        }

        [Test]
        public void LegionStatesItsDamageRange() =>
            Assert.That(typeof(IRaidBossTraits).IsAssignableFrom(typeof(DOL.GS.Scripts.Legion)), Is.True);

        [TestCase(true, true, RaidBossTraits.LairAction.Lead)]
        [TestCase(true, false, RaidBossTraits.LairAction.Lead)]
        [TestCase(false, true, RaidBossTraits.LairAction.Return)]
        [TestCase(false, false, RaidBossTraits.LairAction.Hold)]
        public void OutsideItsLairTheChasedBotLeadsTheOthersComeBackOrWait(bool chased, bool botOutside,
            RaidBossTraits.LairAction expected) =>
            Assert.That(RaidBossTraits.OutsideLairAction(chased, botOutside), Is.EqualTo(expected));

        [Test]
        public void ABotOutsideTheLairWalksBackJustInsideOnItsOwnSide()
        {
            (int x, int y) = RaidBossTraits.PointInsideCircle(1000, 1000, 1000 + 2400, 1000, 2000, RaidBossTraits.LairReturnMargin);
            Assert.That((x, y), Is.EqualTo((1000 + 1700, 1000)));
            (x, y) = RaidBossTraits.PointInsideCircle(0, 0, 0, -3000, 2000, 300);
            Assert.That((x, y), Is.EqualTo((0, -1700)));
            // already well inside: stays where it is
            Assert.That(RaidBossTraits.PointInsideCircle(0, 0, 500, 500, 2000, 300), Is.EqualTo((500, 500)));
        }

        [TestCase(typeof(AlbGolestandt))]
        [TestCase(typeof(MidGjalpinulva))]
        [TestCase(typeof(HibCuuldurach))]
        public void TheDragonsStateTheirLairCircle(System.Type dragon) =>
            Assert.That(typeof(IRaidBossTraits).IsAssignableFrom(dragon), Is.True);
        [TestCase(true, false, false, true)]
        [TestCase(false, true, false, true)]
        [TestCase(false, false, true, true)]
        [TestCase(false, false, false, false)]
        public void NoBladeturnWhileTheFightsBossConsumesIt(bool own, bool kill, bool party, bool blocked) =>
            Assert.That(RaidBossTraits.BladeturnFeedsBoss(own, kill, party), Is.EqualTo(blocked));

        [Test]
        public void AWardenWithoutItsBladeturnChantHoldsItsDamageAdd() =>
            // the brain drops the bladeturn chant from the Warden's list while Legion is the fight's boss
            Assert.That(BotSongTwistPolicy.WardenAnchor(false, true, true, false), Is.EqualTo(eSpellType.DamageAdd));

        [Test]
        public void OnlyLegionConsumesBladeturnByDefault()
        {
            var plain = new PlainBoss();
            Assert.That(((IRaidBossTraits)plain).RaidConsumesBladeturn, Is.False);
            Assert.That(((IRaidBossTraits)plain).RaidMaxDamageRange, Is.Zero);
            Assert.That(((IRaidBossTraits)plain).RaidLairRadius, Is.Zero);
        }

        private sealed class PlainBoss : IRaidBossTraits { }
    }
}
