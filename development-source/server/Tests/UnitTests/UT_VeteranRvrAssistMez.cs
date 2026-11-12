using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Veteran review 2026-11-11, Q22: RvR assist calls skip mezzed enemies; mezzers leave the called target alone.</summary>
    [TestFixture]
    public class UT_VeteranRvrAssistMez
    {
        [Test]
        public void AMezzedEnemyIsNeverTheCall() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrAssist.Callable(false), Is.True);
            Assert.That(AutonomousRvrAssist.Callable(true), Is.False);
        });

        [Test]
        public void ACallWithNoGroupIsNothing() =>
            Assert.That(AutonomousRvrAssist.CalledTarget(null, 0), Is.Null);
    }
}
