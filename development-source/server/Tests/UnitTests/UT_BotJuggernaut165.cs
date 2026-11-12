using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-11: Juggernaut (Prima: "Summons powerful pet for 4 minutes") for gamebot and companion Cabalists,
    /// beside their simulacrum.
    /// </summary>
    [TestFixture]
    public class UT_BotJuggernaut165
    {
        [Test]
        public void ItIsKeptForAFightThatMatters() => Assert.Multiple(() =>
        {
            Assert.That(BotJuggernaut165.Wanted(inCombat: true, controlled: false, enemyRealmFight: true, healthPercent: 100, groupBurst: false), Is.True);
            Assert.That(BotJuggernaut165.Wanted(true, false, false, 45, false), Is.True, "the Cabalist itself is losing");
            Assert.That(BotJuggernaut165.Wanted(true, false, false, 100, true), Is.True, "its group takes burst damage");
            Assert.That(BotJuggernaut165.Wanted(true, false, false, 80, false), Is.False, "an ordinary fight: the 30-minute timer is kept");
            Assert.That(BotJuggernaut165.Wanted(false, false, true, 30, true), Is.False, "never out of combat");
            Assert.That(BotJuggernaut165.Wanted(true, true, true, 30, true), Is.False, "not while mezzed or stunned");
        });

        [Test]
        public void ItFightsWhatTheSimulacrumFightsElseWhatItsOwnerFights() => Assert.Multiple(() =>
        {
            Assert.That(BotJuggernaut165.AssistTarget("pet target", true, "owner target", true), Is.EqualTo("pet target"));
            Assert.That(BotJuggernaut165.AssistTarget("pet target", false, "owner target", true), Is.EqualTo("owner target"),
                "the simulacrum is not attacking (held back, dead, none)");
            Assert.That(BotJuggernaut165.AssistTarget<string>(null, true, "owner target", true), Is.EqualTo("owner target"));
            Assert.That(BotJuggernaut165.AssistTarget("pet target", false, "owner target", false), Is.Null, "nobody fights: it follows");
        });

        [Test]
        public void ItLeavesWithItsOwner() => Assert.Multiple(() =>
        {
            Assert.That(BotJuggernaut165.OwnerHolds(true, true, true), Is.True);
            Assert.That(BotJuggernaut165.OwnerHolds(false, true, true), Is.False, "owner dead");
            Assert.That(BotJuggernaut165.OwnerHolds(true, false, true), Is.False, "owner logged off");
            Assert.That(BotJuggernaut165.OwnerHolds(true, true, false), Is.False, "owner zoned");
        });

        [Test]
        public void BotsBuyAndUseIt()
        {
            Assert.That(AutonomousBotRealmAbilities.ClassicActives, Does.Contain(AutonomousBotRealmAbilities.Juggernaut));
            Assert.That(AutonomousBotRealmAbilities.UsableActivesFor(false), Does.Not.Contain(AutonomousBotRealmAbilities.Juggernaut),
                "True 1.65 Mode only, as the other class actives");
            var fight = new AutonomousBotRealmAbilities.Situation(InCombat: true, Battleground: false, Health: 40, Mana: 80, Controlled: false,
                Sitting: false, Casting: false, Interrupted: false, GroupNear: 1, GroupHurt: 1, GroupFighting: true, DeadMemberNear: false);
            Assert.That(AutonomousBotRealmAbilities.Wants(AutonomousBotRealmAbilities.Juggernaut, fight), Is.True);
            Assert.That(AutonomousBotRealmAbilities.Wants(AutonomousBotRealmAbilities.Juggernaut, fight with { Health = 90, GroupHurt = 0 }), Is.False);
        }
    }
}
