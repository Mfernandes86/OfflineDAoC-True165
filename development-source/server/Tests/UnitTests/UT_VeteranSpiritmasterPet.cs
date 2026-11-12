using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q25: a grouped Spiritmaster with a tank sends its pet only at adds.</summary>
    [TestFixture]
    public class UT_VeteranSpiritmasterPet
    {
        [Test]
        public void HeldForAddsOnlyInAProperGroup() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPetSupport.HoldsPetForAdds(true, true, true), Is.True);
            Assert.That(AutonomousPetSupport.HoldsPetForAdds(true, false, true), Is.False, "small group: the pet fights");
            Assert.That(AutonomousPetSupport.HoldsPetForAdds(true, true, false), Is.False, "no tank: the pet tanks");
            Assert.That(AutonomousPetSupport.HoldsPetForAdds(false, true, true), Is.False, "True 1.65 Mode only");
        });
    }
}
