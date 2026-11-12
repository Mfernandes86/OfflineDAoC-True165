using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Keep sieges by gamebots, fixes from the keeps and relics audits of 2026-11-11.</summary>
[TestFixture]
public class UT_KeepRaidFixes
{
    [TestCase(false, false, ExpectedResult = false, TestName = "An ordinary keep without a lord cannot be captured")]
    [TestCase(false, true, ExpectedResult = true, TestName = "An ordinary keep with its lord can be captured")]
    [TestCase(true, false, ExpectedResult = true, TestName = "A relic keep has no lord: its relic is the prize")]
    public bool OnlyCapturableKeepsAreSiegeTargets(bool relic, bool hasLord) =>
        AutonomousRvrKeepPolicy.IsCapturable(relic, hasLord);
}
