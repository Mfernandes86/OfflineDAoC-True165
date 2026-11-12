using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>GitHub #53: /fly survives a region change (the flight state is sent again on world init).</summary>
[TestFixture]
public class UT_FlyAcrossRegions
{
    [Test]
    public void FlightInProgressIsSentAgainOnARegionChange() => Assert.Multiple(() =>
    {
        Assert.That(FlyCommandHandler.ResendsFlight(true), Is.True);
        Assert.That(FlyCommandHandler.ResendsFlight(false), Is.False, "nobody else gets the flight byte");
    });

    [Test]
    public void TheFlightByteSaysOnOrOffOnlyForAGm() => Assert.Multiple(() =>
    {
        Assert.That(FlyCommandHandler.FlightByte(true, true), Is.EqualTo(1));
        Assert.That(FlyCommandHandler.FlightByte(true, false), Is.EqualTo(2));
        Assert.That(FlyCommandHandler.FlightByte(false, true), Is.EqualTo(2), "a player account never flies");
    });
}
