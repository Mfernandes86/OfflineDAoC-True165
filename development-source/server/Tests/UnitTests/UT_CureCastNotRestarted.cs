using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// GitHub #45: a companion Healer never finished Cure Nearsight I (a 6-second cast) - on every think it chose the cure
    /// again (the nearsight is on the target until the cast lands) and cancelled its own cast to start it over.
    /// </summary>
    [TestFixture]
    public class UT_CureCastNotRestarted
    {
        private static Spell Cure(int id) =>
            new(new DbSpell { SpellID = id, Name = "Cure Nearsight", Type = "CureNearsightCustom", Target = "Realm", CastTime = 6 }, 20);

        [Test]
        public void TheSameCureOnTheSameTargetGoesOn() => Assert.Multiple(() =>
        {
            Spell cure = Cure(1), sameRow = Cure(1), other = Cure(2);
            object blinded = new(), someoneElse = new();
            Assert.That(BotBrain.KeepsCastInProgress(true, cure, cure, blinded, blinded), Is.True, "left to finish");
            Assert.That(BotBrain.KeepsCastInProgress(true, cure, sameRow, blinded, blinded), Is.True, "the same spell by id");
            Assert.That(BotBrain.KeepsCastInProgress(true, cure, cure, blinded, someoneElse), Is.False, "another member needs it: as before");
            Assert.That(BotBrain.KeepsCastInProgress(true, other, cure, blinded, blinded), Is.False, "another spell: as before");
            Assert.That(BotBrain.KeepsCastInProgress(false, cure, cure, blinded, blinded), Is.False, "not casting");
            Assert.That(BotBrain.KeepsCastInProgress(true, null, cure, blinded, blinded), Is.False);
            Assert.That(BotBrain.KeepsCastInProgress(true, cure, cure, null, null), Is.False, "no target known");
        });
    }
}
