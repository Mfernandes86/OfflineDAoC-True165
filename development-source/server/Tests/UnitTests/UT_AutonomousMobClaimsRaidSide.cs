using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>dragon-fight L2: a raid is one side for the claim rules; an ordinary group keeps its own side.</summary>
    [TestFixture]
    public class UT_AutonomousMobClaimsRaidSide
    {
        private static readonly object RaidA = new(), RaidB = new(), GroupA = new(), GroupB = new();

        [Test]
        public void SisterPartiesOfOneRaidAreOneSide() =>
            Assert.That(AutonomousMobClaims.SameSide(RaidA, RaidA, true, false), Is.True);

        [Test]
        public void TwoRaidsAreTwoSides() =>
            Assert.That(AutonomousMobClaims.SameSide(RaidA, RaidB, true, false), Is.False);

        [Test]
        public void ARealmPlayerFightingBesideTheRaidIsItsSide() =>
            Assert.That(AutonomousMobClaims.SameSide(RaidA, new object(), true, true), Is.True);

        [Test]
        public void OrdinaryGroupsAreNotLoosened()
        {
            Assert.That(AutonomousMobClaims.SameSide(GroupA, GroupA, false, false), Is.True);
            Assert.That(AutonomousMobClaims.SameSide(GroupA, GroupB, false, false), Is.False);
            // a player of the realm on someone else's monster is still someone else for an ordinary group
            Assert.That(AutonomousMobClaims.SameSide(GroupA, new object(), false, true), Is.False);
        }

        [Test]
        public void ASoloBotIsItsOwnSide() =>
            Assert.That(AutonomousMobClaims.SideOf(null), Is.Null);
    }
}
