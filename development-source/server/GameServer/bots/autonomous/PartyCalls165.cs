using System;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Party calls (veteran-bots review 2026-11-11, M10 / rvr-social G11). Bots in a player's party answered the player but
    /// never made the calls a 2003 group lived on: "pulling", "inc", "oom", "mezzing add", "rez pls". They now make them on
    /// the state changes they already go through, in party chat, only when the party has a player in it:
    /// - the puller starts a pull (autonomous party pull, or the tank of a player's /pull): "pulling {target}";
    /// - a caster's cast leaves it under 15% power: "oom";
    /// - a mesmerize cast on an add: "mezzing add, don't break it";
    /// - a bot dies with a living resurrecting class in the party: "rez pls", a few seconds later.
    /// At most one call per party per 8 seconds, "oom" once a minute per bot. No new tick; every trigger is an existing
    /// code path.
    /// </summary>
    public static class PartyCalls165
    {
        public const long PartyIntervalMilliseconds = 8_000;
        public const long OomIntervalMilliseconds = 60_000;
        public const int OomPercent = 15;

        private sealed class Box
        {
            public long Next;
            public int LastPower = 100;
        }

        private static readonly ConditionalWeakTable<Group, Box> PartyNext = new();
        private static readonly ConditionalWeakTable<GameBot, Box> OomNext = new();

        /// <summary>The classes with a resurrection spell in 1.65.</summary>
        public static bool CanResurrect(eCharacterClass cls) => cls is eCharacterClass.Cleric or eCharacterClass.Friar or
            eCharacterClass.Healer or eCharacterClass.Shaman or eCharacterClass.Druid or eCharacterClass.Warden or
            eCharacterClass.Bard or eCharacterClass.Paladin;

        /// <summary>Whether a cast that leaves the bot at this power is the moment to call "oom" (a crossing, not every cast below).</summary>
        public static bool CrossedOom(int powerBefore, int powerAfter) => powerBefore >= OomPercent && powerAfter < OomPercent;

        public static string PullLine(string target, Random random) => (random ?? Random.Shared).Next(4) switch
        {
            0 => string.IsNullOrWhiteSpace(target) ? "pulling" : $"pulling {target}",
            1 => "inc",
            2 => string.IsNullOrWhiteSpace(target) ? "pulling, get ready" : $"inc {target}",
            _ => "pulling, rdy?",
        };

        public static string OomLine(Random random) => (random ?? Random.Shared).Next(3) switch
        {
            0 => "oom",
            1 => "oom, need a min",
            _ => "low pow",
        };

        private static Group PlayerParty(GameBot bot)
        {
            Group group = bot?.Group;
            return group != null && group.GetMembersInTheGroup().Any(m => m is GamePlayer) ? group : null;
        }

        private static bool Say(GameBot bot, Group group, string line)
        {
            if (group == null || string.IsNullOrWhiteSpace(line))
                return false;
            long now = GameLoop.GameLoopTime;
            Box box = PartyNext.GetValue(group, _ => new Box { Next = long.MinValue });
            if (box.Next != long.MinValue && now < box.Next)
                return false;
            box.Next = now + PartyIntervalMilliseconds;
            group.SendMessageToGroupMembers(bot, line, eChatType.CT_Group, eChatLoc.CL_ChatWindow);
            FeatureCounters165.Add("party_call");
            return true;
        }

        /// <summary>The puller starts a pull.</summary>
        public static void OnPull(GameBot puller, GameLiving target)
        {
            try
            {
                if (PlayerParty(puller) is { } group)
                    Say(puller, group, PullLine(target?.Name, Random.Shared));
            }
            catch (Exception) { }
        }

        /// <summary>GameBot.CastSpell: a cast started. Power is compared with the bot's power at its previous cast.</summary>
        public static void OnCastStarted(GameBot bot, Spell spell)
        {
            try
            {
                if (spell == null || PlayerParty(bot) is not { } group)
                    return;
                if (spell.SpellType == eSpellType.Mesmerize && bot.TargetObject is GameNPC add &&
                    group.GetMembersInTheGroup().Any(m => m != bot && m.TargetObject != add && m.TargetObject is GameNPC))
                {
                    Say(bot, group, "mezzing add, don't break it");
                    return;
                }
                if (bot.MaxMana <= 0)
                    return;
                Box box = OomNext.GetValue(bot, _ => new Box { Next = long.MinValue });
                int power = bot.ManaPercent;
                int before = box.LastPower;
                box.LastPower = power;
                if (CrossedOom(before, power))
                {
                    long now = GameLoop.GameLoopTime;
                    if (box.Next != long.MinValue && now < box.Next)
                        return;
                    if (Say(bot, group, OomLine(Random.Shared)))
                        box.Next = now + OomIntervalMilliseconds;
                }
            }
            catch (Exception) { }
        }

        /// <summary>GameBot.ProcessDeath: "rez pls" when a resurrecting class of the party is alive.</summary>
        public static void OnDeath(GameBot bot)
        {
            try
            {
                if (PlayerParty(bot) is not { } group)
                    return;
                bool rezzer = group.GetMembersInTheGroup().Any(m => m != bot && m.IsAlive &&
                    m switch
                    {
                        GamePlayer p => p.CharacterClass != null && CanResurrect((eCharacterClass)p.CharacterClass.ID),
                        GameBot b => b.CharacterClass != null && CanResurrect((eCharacterClass)b.CharacterClass.ID),
                        _ => false,
                    });
                if (!rezzer)
                    return;
                _ = new ECSGameTimer(bot, _ =>
                {
                    if (!bot.IsAlive && bot.Group == group)
                        Say(bot, group, Random.Shared.Next(3) switch { 0 => "rez pls", 1 => "rez?", _ => "can i get a rez" });
                    return 0;
                }, 3_000 + Random.Shared.Next(2_500));
            }
            catch (Exception) { }
        }
    }
}
