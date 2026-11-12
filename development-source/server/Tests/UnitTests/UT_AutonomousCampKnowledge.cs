using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review M11: camp traits worked out by the background builder, and how camp choices weigh them.</summary>
[TestFixture]
public sealed class UT_AutonomousCampKnowledge
{
    private static AutonomousCampKnowledge.Member M(float x, float y, int aggro = 400, bool caster = false, byte body = 1) =>
        new(new Vector3(x, y, 0), aggro, caster, body);

    [Test]
    public void PackFactorCountsNeighboursWithinReach()
    {
        // three monsters 200 apart (each sees the other two), one alone far away
        var traits = AutonomousCampKnowledge.Measure(new[] { M(0, 0), M(200, 0), M(0, 200), M(3000, 0) }, Vector3.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(traits.PackFactor, Is.EqualTo(6f / 4f).Within(1e-4));
            Assert.That(traits.Radius, Is.EqualTo(3000));
            Assert.That(traits.Known, Is.True);
            Assert.That(traits.BodyType, Is.EqualTo(1));
        });
    }

    [Test]
    public void CasterShareAndMixedBodies()
    {
        var traits = AutonomousCampKnowledge.Measure(new[] { M(0, 0, caster: true, body: 9), M(5000, 0, caster: true, body: 7),
            M(9000, 0, body: 1) }, Vector3.Zero);
        Assert.That(traits.CasterShare, Is.EqualTo(2f / 3f).Within(1e-4));
        Assert.That(traits.PackFactor, Is.Zero);
        Assert.That(traits.BodyType, Is.EqualTo(1).Or.EqualTo(7).Or.EqualTo(9), "no majority beyond a third each: one of them");
    }

    [Test]
    public void UnknownTraitsNeverChangeTheDraw()
    {
        Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Armsman, default), Is.EqualTo(1));
        Assert.That(AutonomousCampKnowledge.GroupFactor(false, default), Is.EqualTo(1));
        Assert.That(AutonomousCampKnowledge.Measure(new AutonomousCampKnowledge.Member[0], Vector3.Zero).Known, Is.False);
    }

    [Test]
    public void DenseCampsAreForPartiesWithCrowdControl()
    {
        var dense = new AutonomousCampKnowledge.Traits(2.5f, 0, 1, 1000, 6);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousCampKnowledge.GroupFactor(false, dense), Is.EqualTo(0.5));
            Assert.That(AutonomousCampKnowledge.GroupFactor(true, dense), Is.EqualTo(1));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Warrior, dense), Is.EqualTo(0.5));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Wizard, dense), Is.EqualTo(0.75));
        });
    }

    [Test]
    public void ClassesPreferTheirPreyAndMeleeAvoidCasters()
    {
        var animals = new AutonomousCampKnowledge.Traits(0.5f, 0, 1, 1000, 6);
        var reptiles = new AutonomousCampKnowledge.Traits(0.5f, 0, 9, 1000, 6);
        var casters = new AutonomousCampKnowledge.Traits(0.5f, 0.8f, 6, 1000, 6);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Hunter, animals), Is.EqualTo(1.5));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Spiritmaster, reptiles), Is.EqualTo(1.5));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Spiritmaster, animals), Is.EqualTo(1));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Hero, casters), Is.EqualTo(0.5));
            Assert.That(AutonomousCampKnowledge.SoloFactor(eCharacterClass.Cleric, casters), Is.EqualTo(1));
        });
    }

    [Test]
    public void SoloDungeonChanceFollowsTheClass()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousCampKnowledge.SoloDungeonPermille(eCharacterClass.Necromancer), Is.EqualTo(150));
            Assert.That(AutonomousCampKnowledge.SoloDungeonPermille(eCharacterClass.Shadowblade), Is.EqualTo(150));
            Assert.That(AutonomousCampKnowledge.SoloDungeonPermille(eCharacterClass.Healer), Is.EqualTo(30));
            Assert.That(AutonomousCampKnowledge.SoloDungeonPermille(eCharacterClass.Armsman),
                Is.EqualTo(AutonomousBotDecisionEngine.SoloDungeonPreferencePermille));
        });
    }
}
