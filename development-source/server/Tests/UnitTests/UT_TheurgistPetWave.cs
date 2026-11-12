using NUnit.Framework;
using static DOL.GS.AutonomousPetSupport;

namespace DOL.GS.Tests
{
    /// <summary>
    /// M8 (veteran review 2026-11-11): a Theurgist calls an earth elemental first (it holds the aggro), then its own spec
    /// line, three pets a wave (Prima: "three pets, step back, three more"), and nukes until that wave is gone.
    /// </summary>
    [TestFixture]
    public sealed class UT_TheurgistPetWave
    {
        [Test]
        public void EarthFirstThenTheSpecLineUpToThree() => Assert.Multiple(() =>
        {
            Assert.That(TheurgistNextSummon(0, false, false, TheurgistElement.Air), Is.EqualTo(TheurgistElement.Earth));
            Assert.That(TheurgistNextSummon(1, false, false, TheurgistElement.Air), Is.EqualTo(TheurgistElement.Air));
            Assert.That(TheurgistNextSummon(2, false, false, TheurgistElement.Ice), Is.EqualTo(TheurgistElement.Ice));
            Assert.That(TheurgistNextSummon(3, false, false, TheurgistElement.Ice), Is.Null, "three up: nuke");
            Assert.That(TheurgistNextSummon(2, false, false, TheurgistElement.Earth), Is.EqualTo(TheurgistElement.Earth),
                "an Earth Theurgist stays on earth");
        });

        [Test]
        public void AMonsterOnTheTheurgistGetsOnlyTheEarthPet() => Assert.Multiple(() =>
        {
            Assert.That(TheurgistNextSummon(0, false, true, TheurgistElement.Air), Is.EqualTo(TheurgistElement.Earth));
            Assert.That(TheurgistNextSummon(1, false, true, TheurgistElement.Air), Is.Null);
        });

        [Test]
        public void AFullWaveWaitsUntilEveryPetIsGone() => Assert.Multiple(() =>
        {
            Assert.That(TheurgistWaveFull(false, 3), Is.True);
            Assert.That(TheurgistWaveFull(true, 2), Is.True, "one of the three died: keep nuking");
            Assert.That(TheurgistWaveFull(true, 0), Is.False, "the wave is gone: the next one starts");
            Assert.That(TheurgistWaveFull(false, 2), Is.False, "still building the wave");
            Assert.That(TheurgistNextSummon(2, true, false, TheurgistElement.Air), Is.Null);
        });
    }
}
