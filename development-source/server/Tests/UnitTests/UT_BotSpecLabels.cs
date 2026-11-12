using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.ServerRules;
using DOL.UnitTests;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>Veteran review Q35: spec names per spec, honest build labels, and the pure two-handed Armsman label.</summary>
[TestFixture, NonParallelizable]
public sealed class UT_BotSpecLabels
{
    // Weapon lines resolve through SkillBase, which needs a server and its language strings (as UT_ClassBuilds165).
    private sealed class SpecTestServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    private GameServer _previousServer;
    private PetTestLanguageScope _language;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        _language = new PetTestLanguageScope();
        GameServer.LoadTestDouble((SpecTestServer)RuntimeHelpers.GetUninitializedObject(typeof(SpecTestServer)));
    }

    [TearDown]
    public void TearDown()
    {
        GameServer.LoadTestDouble(_previousServer);
        _language.Dispose();
    }

    [Test]
    public void EachSpecKeepsItsOwnName()
    {
        BotSpec armsman = BotSpec.GetSpec(eCharacterClass.Armsman);
        BotSpec wizard = BotSpec.GetSpec(eCharacterClass.Wizard);
        Assert.That(armsman.SpecName, Is.EqualTo("ArmsmanBotSpec"), "building a second spec no longer renames the first");
        Assert.That(wizard.SpecName, Is.EqualTo("WizardBotSpec"));
    }

    [Test]
    public void BuildLabelNamesTheTwoHighestLines()
    {
        var lines = new[] { new BotSpecLine("Stealth", 37, 0.5f), new BotSpecLine("Critical Strike", 50, 0.9f),
            new BotSpecLine("Dual Wield", 25, 0.2f), new BotSpecLine("Envenom", 37, 0.6f) };
        Assert.That(BotSpec.BuildLabelOf(lines), Is.EqualTo("Critical Strike/Envenom"));
        Assert.That(BotSpec.BuildLabelOf(null), Is.Empty);
    }

    [Test]
    public void PureTwoHandedArmsmanIsLabelledTwoHanded()
    {
        for (int i = 0; i < 20; i++)
        {
            BotSpec spec = BotSpec.GetSpec(eCharacterClass.Armsman, eSpecType.TwoHanded);
            Assert.That(spec.SpecType, Is.EqualTo(eSpecType.TwoHanded));
            Assert.That(spec.Is2H, Is.True);
            Assert.That(spec.SpecLines.Exists(line => line.Spec == Specs.Shields), Is.False);
        }
        BotSpec hybrid = BotSpec.GetSpec(eCharacterClass.Armsman, eSpecType.TwoHandHybrid);
        Assert.That(hybrid.SpecType, Is.EqualTo(eSpecType.TwoHandHybrid));
        Assert.That(hybrid.SpecLines.Exists(line => line.Spec == Specs.Shields), Is.True);
    }
}
