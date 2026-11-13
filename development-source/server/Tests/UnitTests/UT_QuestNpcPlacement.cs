using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Gamefix QNPC (2026-11-12): scripted quests look for their NPC in the world first and create it only when none
/// stands at the spot. Lost Stone of Arawn (N'chever) and The Lost Seed (Emolia, Jandros) searched a spot copied
/// from another NPC, never where they create it, so a world NPC there was never found and a twin stood beside it.
/// </summary>
[TestFixture]
public class UT_QuestNpcPlacement
{
    private class SpotNpc : GameNPC
    {
        public ushort TestRegion;
        public int TestX, TestY;
        public override ushort CurrentRegionID { get => TestRegion; set { } }
        public override int X => TestX;
        public override int Y => TestY;
    }

    private sealed class SpotTeleporter : HiberniaSITeleporter
    {
        public ushort TestRegion;
        public int TestX, TestY;
        public override ushort CurrentRegionID { get => TestRegion; set { } }
        public override int X => TestX;
        public override int Y => TestY;
    }

    private static T At<T>(ushort region, int x, int y) where T : GameNPC
    {
        T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        switch (npc)
        {
            case SpotNpc s: s.TestRegion = region; s.TestX = x; s.TestY = y; break;
            case SpotTeleporter t: t.TestRegion = region; t.TestX = x; t.TestY = y; break;
        }
        return npc;
    }

    [Test]
    public void Find_ReturnsTheNpcStandingAtTheSpot()
    {
        SpotNpc far = At<SpotNpc>(181, 404696, 503469), near = At<SpotNpc>(181, 310900, 349900);
        Assert.That(QuestNpcPlacement.Find<GameNPC>(new GameNPC[] { far, near }, 181, 310873, 349961), Is.SameAs(near));
    }

    [Test]
    public void Find_IgnoresOtherRegionsAndSpotsBeyondTheRadius()
    {
        int r = QuestNpcPlacement.SearchRadius;
        var npcs = new GameNPC[] { At<SpotNpc>(51, 435972, 492370), At<SpotNpc>(181, 435972 + r + 1, 492370), At<SpotNpc>(181, 435972, 492370 - r - 1) };
        Assert.That(QuestNpcPlacement.Find<GameNPC>(npcs, 181, 435972, 492370), Is.Null);
        Assert.That(QuestNpcPlacement.Find<GameNPC>(new GameNPC[] { At<SpotNpc>(181, 435972 + r, 492370 - r) }, 181, 435972, 492370), Is.Not.Null);
    }

    [Test]
    public void Find_SkipsANpcOfAnotherClassInsteadOfFailingACast()
    {
        SpotNpc plain = At<SpotNpc>(181, 378864, 421086);
        SpotTeleporter teleporter = At<SpotTeleporter>(181, 378870, 421090);
        Assert.That(QuestNpcPlacement.Find<HiberniaSITeleporter>(new GameNPC[] { plain }, 181, 378864, 421086), Is.Null);
        Assert.That(QuestNpcPlacement.Find<HiberniaSITeleporter>(new GameNPC[] { plain, teleporter }, 181, 378864, 421086), Is.SameAs(teleporter));
    }

    [Test]
    public void Find_HandlesNoNpcs()
    {
        Assert.That(QuestNpcPlacement.Find<GameNPC>(null, 1, 0, 0), Is.Null);
        Assert.That(QuestNpcPlacement.Find<GameNPC>(new List<GameNPC>(), 1, 0, 0), Is.Null);
    }

    // ---- every quest script searches the spot it creates the NPC at ----

    private static readonly Regex Lookup = new(@"GetNPCsByName\(\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    private static readonly Regex Create = new(@"(\w+)\s*=\s*new\s+\w+\s*\(\s*\)", RegexOptions.Compiled);
    private static readonly Regex FindSpot = new(@"^GetNPCsByName\([^)]*\)\s*,\s*(\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex IsAtSpot = new(@"IsAt\(\s*\w+\s*,\s*(\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled);

    private static string QuestScripts()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "GameServer", "scripts", "quests");
            if (Directory.Exists(path))
                return path;
        }
        return null;
    }

    private static int? Assigned(string block, string var, string field)
    {
        Match m = Regex.Match(block, @"\b" + Regex.Escape(var) + @"\." + field + @"\s*=\s*(-?\d+)\s*;");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    [Test]
    public void EveryQuestScriptSearchesTheSpotItCreatesTheNpcAt()
    {
        string root = QuestScripts();
        if (root == null)
            Assert.Ignore("GameServer/scripts/quests not found from the test directory");

        var checkedNpcs = new List<string>();
        var wrong = new List<string>();
        foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (Match look in Lookup.Matches(text))
            {
                int end = text.IndexOf("AddToWorld(", look.Index, System.StringComparison.Ordinal);
                string block = text.Substring(look.Index, (end < 0 ? text.Length : end) - look.Index);
                Match create = Create.Match(block);
                if (!create.Success)
                    continue;   // looks the NPC up only (it must exist), nothing to compare
                string before = block[..create.Index];
                Match spot = FindSpot.Match(before);
                if (!spot.Success)
                    spot = IsAtSpot.Match(before);
                if (!spot.Success)
                    continue;   // a spot given by variables (Mystic50 Danica tries two spots)
                string after = block[create.Index..];
                string var = create.Groups[1].Value;
                int? x = Assigned(after, var, "X"), y = Assigned(after, var, "Y"), region = Assigned(after, var, "CurrentRegionID");
                if (x == null || y == null)
                    continue;
                string name = Path.GetFileNameWithoutExtension(file) + ":" + look.Groups[1].Value.Replace("\\'", "'");
                checkedNpcs.Add(name);
                int lr = int.Parse(spot.Groups[1].Value), lx = int.Parse(spot.Groups[2].Value), ly = int.Parse(spot.Groups[3].Value);
                if ((region != null && region != lr) ||
                    System.Math.Abs(lx - x.Value) > QuestNpcPlacement.SearchRadius || System.Math.Abs(ly - y.Value) > QuestNpcPlacement.SearchRadius)
                    wrong.Add($"{name} searches {lr} {lx},{ly} but creates at {region} {x},{y}");
            }
        }

        Assert.That(wrong, Is.Empty, "a script that searches elsewhere spawns a twin beside the world NPC");
        Assert.That(checkedNpcs, Has.Count.GreaterThanOrEqualTo(40), "the scan stopped recognising the scripts");
        Assert.That(checkedNpcs, Does.Contain("LostStoneOfArawn:N'chever"));
        Assert.That(checkedNpcs, Does.Contain("TheLostSeed:Emolia"));
        Assert.That(checkedNpcs, Does.Contain("TheLostSeed:Jandros"));
    }
}
