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

        [TestCase(eSpellTarget.AREA, 2800, 800, RaidBossTraits.AreaCentre.Ground)]     // Apocalypse's Rain of Fire
        [TestCase(eSpellTarget.AREA, 0, 350, RaidBossTraits.AreaCentre.Caster)]        // point blank
        [TestCase(eSpellTarget.ENEMY, 1500, 300, RaidBossTraits.AreaCentre.CastTarget)] // Govannon's damage over time
        [TestCase(eSpellTarget.ENEMY, 1500, 0, RaidBossTraits.AreaCentre.None)]         // a plain bolt
        [TestCase(eSpellTarget.CONE, 1000, 500, RaidBossTraits.AreaCentre.None)]
        public void TheAreaOfABossSpellIsWhereItLands(eSpellTarget target, int range, int radius,
            RaidBossTraits.AreaCentre expected) =>
            Assert.That(RaidBossTraits.CentreOf(target, range, radius), Is.EqualTo(expected));

        // inside, castTargetOfTargetedSpell, ranged, holdsBoss, health, centredOnBoss, canHitBoss
        [TestCase(true, false, true, false, 100, true, false, true)]    // ranged or healer: always out
        [TestCase(true, false, false, true, 90, false, true, false)]    // the tank holding it stays while healthy
        [TestCase(true, false, false, true, 40, false, true, true)]     // ... and leaves when low
        [TestCase(true, false, false, false, 100, false, true, true)]   // melee beside the tank: out of the area round it
        [TestCase(true, false, false, false, 100, true, true, false)]   // melee at a point blank boss it can hit: stays
        [TestCase(true, false, false, false, 100, true, false, true)]   // melee under Apocalypse in the air: out
        [TestCase(true, true, true, false, 100, false, true, false)]    // the spell's own target: it follows it, stay
        [TestCase(false, false, true, false, 100, true, false, false)]  // not inside: nothing to do
        public void ABotStepsOutWhenItCanKeepFightingFromOutside(bool inside, bool castTarget, bool ranged, bool holds,
            int health, bool onBoss, bool canHit, bool expected) =>
            Assert.That(RaidBossTraits.StepsOutOfArea(inside, castTarget, ranged, holds, health, onBoss, canHit),
                Is.EqualTo(expected));

        [Test]
        public void AMeleeBotWaitsOutsideWhileTheBossStandsInTheArea()
        {
            Assert.That(RaidBossTraits.WaitsOutsideArea(true, false, false, false, true), Is.True);
            Assert.That(RaidBossTraits.WaitsOutsideArea(false, false, false, false, true), Is.False);  // cast over: back in
            Assert.That(RaidBossTraits.WaitsOutsideArea(true, false, true, false, true), Is.False);   // ranged keep casting
            Assert.That(RaidBossTraits.WaitsOutsideArea(true, false, false, false, false), Is.False); // boss is outside it
        }

        [Test]
        public void ABotStepsStraightOutOfTheCircle()
        {
            Assert.That(RaidBossTraits.PointOutsideCircle(0, 0, 100, 0, 800, 150, 0, 0), Is.EqualTo((950, 0)));
            // on the centre: away from the boss
            Assert.That(RaidBossTraits.PointOutsideCircle(0, 0, 0, 0, 300, 150, 0, 100), Is.EqualTo((0, -450)));
            // on the centre with the boss there too: east
            Assert.That(RaidBossTraits.PointOutsideCircle(0, 0, 0, 0, 300, 150, 0, 0), Is.EqualTo((450, 0)));
        }

        [TestCase(true, true, true)]     // Lich Lord Sanguis at 5%, already being fought
        [TestCase(false, true, false)]   // a bot that never had him cannot pick him now
        [TestCase(false, false, false)]  // the fire primal's patches: untargetable from the start
        [TestCase(true, false, false)]   // an ordinary monster that turned untargetable is dropped as before
        public void ABossThatTurnsUntargetableMidFightIsKeptByItsFighters(bool onList, bool boss, bool kept) =>
            Assert.That(RaidBossTraits.KeepsFoeThatTurnedUntargetable(onList, boss), Is.EqualTo(kept));

        private sealed class PlainBoss : IRaidBossTraits { }
    }
}
