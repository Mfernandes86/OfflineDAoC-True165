using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review Q30: a player's field-pet target is shared by their companions for 250 ms.</summary>
[TestFixture]
public sealed class UT_CompanionLeaderTargetMemo
{
    [Test]
    public void FieldPetAnswerIsSharedBriefly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlayerLedPullCoordinator.FieldPetMemoFresh(0, 100), Is.False, "never asked yet");
            Assert.That(PlayerLedPullCoordinator.FieldPetMemoFresh(1_000, 1_100), Is.True);
            Assert.That(PlayerLedPullCoordinator.FieldPetMemoFresh(1_000, 1_000 + PlayerLedPullCoordinator.FieldPetMemoMilliseconds), Is.False);
            Assert.That(PlayerLedPullCoordinator.FieldPetMemoFresh(1_000, 900), Is.False, "clock went back");
        });
    }
}
