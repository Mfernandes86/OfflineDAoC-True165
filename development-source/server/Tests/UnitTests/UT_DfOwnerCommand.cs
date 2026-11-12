using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>GitHub #50: /dfowner takes a realm number or name (and is a GM command, not an Admin one).</summary>
[TestFixture]
public class UT_DfOwnerCommand
{
    [TestCase("0", 0)]
    [TestCase("3", 3)]
    [TestCase("none", 0)]
    [TestCase("alb", 1)]
    [TestCase("Albion", 1)]
    [TestCase("mid", 2)]
    [TestCase("midgard", 2)]
    [TestCase("HIB", 3)]
    public void ARealmNumberOrNameIsTaken(string text, int expected)
    {
        Assert.That(DFOwnerCommandHandler.TryParseRealm(text, out byte realm), Is.True);
        Assert.That(realm, Is.EqualTo((byte)expected));
    }

    [TestCase("4")]
    [TestCase("-1")]
    [TestCase("camelot")]
    [TestCase("")]
    [TestCase(null)]
    public void AnythingElseIsRefused(string text) =>
        Assert.That(DFOwnerCommandHandler.TryParseRealm(text, out _), Is.False);
}
