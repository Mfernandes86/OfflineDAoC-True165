using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, Q23: songs fit the group - a Minstrel holds the power song for a caster-heavy group, a
    /// second Skald plays speed (RvR) or regeneration (PvE) instead of doubling the damage chant. Still one song, no twisting.
    /// </summary>
    [TestFixture]
    public class UT_VeteranGroupSongs
    {
        private static readonly HashSet<eSpellType> MinstrelSongs =
            [eSpellType.EnduranceRegenBuff, eSpellType.PowerRegenBuff, eSpellType.HealthRegenBuff, eSpellType.SpeedEnhancement];
        private static readonly HashSet<eSpellType> SkaldSongs =
            [eSpellType.DamageAdd, eSpellType.SpeedEnhancement, eSpellType.HealthRegenBuff];

        [Test]
        public void MinstrelHoldsPowerForCasters() => Assert.Multiple(() =>
        {
            Assert.That(BotSongTwistPolicy.DefaultFightSong(eCharacterClass.Minstrel, MinstrelSongs, true, false, false),
                Is.EqualTo(eSpellType.PowerRegenBuff));
            Assert.That(BotSongTwistPolicy.DefaultFightSong(eCharacterClass.Minstrel, MinstrelSongs, false, false, false),
                Is.EqualTo(eSpellType.EnduranceRegenBuff), "a melee group keeps the endurance song");
        });

        [Test]
        public void ASecondSkaldDoesNotDoubleTheDamageChant() => Assert.Multiple(() =>
        {
            Assert.That(BotSongTwistPolicy.DefaultFightSong(eCharacterClass.Skald, SkaldSongs, false, false, true),
                Is.EqualTo(eSpellType.DamageAdd), "the first Skald");
            Assert.That(BotSongTwistPolicy.DefaultFightSong(eCharacterClass.Skald, SkaldSongs, false, true, true),
                Is.EqualTo(eSpellType.SpeedEnhancement), "RvR: speed");
            Assert.That(BotSongTwistPolicy.DefaultFightSong(eCharacterClass.Skald, SkaldSongs, false, true, false),
                Is.EqualTo(eSpellType.HealthRegenBuff), "PvE: regeneration");
        });

        [Test]
        public void OtherPerformersAreUnchanged() => Assert.Multiple(() =>
        {
            Assert.That(BotSongTwistPolicy.DefaultFightSong(eCharacterClass.Bard, MinstrelSongs, true, false, false),
                Is.EqualTo(eSpellType.EnduranceRegenBuff));
            Assert.That(BotSongTwistPolicy.CasterHeavy(2, 4), Is.True);
            Assert.That(BotSongTwistPolicy.CasterHeavy(1, 4), Is.False);
            Assert.That(BotSongTwistPolicy.CasterHeavy(1, 1), Is.False, "a pair is no caster group");
        });
    }
}
