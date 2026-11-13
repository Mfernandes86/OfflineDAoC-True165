using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-12: every server start put crowds of bots at the Tir na Nog vault keeper. The realms' home regions
    /// (1, 100, 200) are flagged frontier as a whole because the old frontier zones are inside them, and the login and
    /// logout code read that flag. Only the frontier zones count there.
    /// </summary>
    [TestFixture]
    public class UT_BotLoginFrontier
    {
        [Test]
        public void TheRealmCountrysideIsNotFrontier() => Assert.Multiple(() =>
        {
            // Hibernia 200: Shannon Estuary (202) is home land; Mount Collory 210, Cruachan Gorge 211, Breifine 212 and
            // Emain Macha 214 are the frontier
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(200, true, 202), Is.False);
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(200, true, 210), Is.True);
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(200, true, 214), Is.True);
            // Albion 1 (Camelot Hills 0 / Forest Sauvage 11) and Midgard 100 (Vale of Mularn 100 / Uppland 111) the same
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(1, true, 0), Is.False);
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(1, true, 11), Is.True);
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(100, true, 100), Is.False);
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(100, true, 111), Is.True);
            // no zone under the spot in a home region: not frontier (the dungeon / invalid checks deal with it)
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(200, true, null), Is.False);
        });

        [Test]
        public void OtherRegionsKeepTheirOwnFlag() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(201, false, 201), Is.False);   // Tir na Nog
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(181, false, 181), Is.False);   // Hy Brasil
            Assert.That(AutonomousBotLifecycle.IsFrontierPoint(163, true, 163), Is.True);     // a region flagged frontier as a whole
        });
    }
}
