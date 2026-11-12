using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;

namespace DOL.GS;

/// <summary>
/// Coordinates scarce, short bot conversations. Faction text stays broad;
/// local text is allowed to mention the speaker's live task and surroundings.
/// </summary>
public static class AutonomousBotChatCoordinator
{
    private static readonly object Sync = new();
    private static readonly Dictionary<eRealm, long> LastFactionAmbient = new();
    private static readonly Dictionary<ushort, long> LastLocalAmbient = new();
    private static readonly Dictionary<eRealm, long> LastFactionPlayerResponse = new();
    private static readonly Dictionary<ushort, long> LastLocalPlayerResponse = new();

    /// <summary>
    /// The gap between two ambient /say lines in one zone: 90 s with 10 bots or fewer, one second less per bot above
    /// that, never under 30 s (a crowded capital or portal keep).
    /// </summary>
    public static long LocalIntervalMilliseconds(int botsInZone) => Math.Clamp(100 - botsInZone, 30, 90) * 1_000L;

    private static Dictionary<ushort, int> _zoneCounts = new();
    private static long _zoneCountsTick = long.MinValue;

    /// <summary>Live gamebots per zone, counted once a minute from the registry (one pass, integer counts).</summary>
    private static int BotsInZone(ushort zone, long now)
    {
        if (now - _zoneCountsTick >= 60_000 || _zoneCountsTick == long.MinValue)
        {
            var counts = new Dictionary<ushort, int>();
            foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            {
                if (bot?.ObjectState != GameObject.eObjectState.Active)
                    continue;
                ushort id = bot.CurrentZone?.ID ?? bot.CurrentRegionID;
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
            _zoneCounts = counts;
            _zoneCountsTick = now;
        }
        return _zoneCounts.GetValueOrDefault(zone);
    }

    private static readonly string[] FactionOpeners =
    [
        "How fares everyone across the realm?", "Any groups forming today?", "The roads seem lively today.",
        "Anyone heading out for a longer hunt?", "I may look for company later.", "Good hunting, everyone.",
        "Has the frontier been active?", "Anyone trading useful equipment today?", "Stay sharp out there.",
        "Which hunting grounds are treating everyone well?", "Any useful class advice to share today?",
        "Has anyone found a dependable camp for their level?", "The realm feels lively today.",
        "If anyone needs a group, say where you are headed.", "Remember to visit your trainer after leveling.",
        "Are any dungeon groups planning a careful run?", "Safe roads and good hunting to everyone.",
        "What is everyone working on today?",
    ];

    private static readonly string[] FactionReplies =
    [
        "Things seem steady from where I am.", "I have seen a few adventurers moving around.",
        "I may be available once I finish up here.", "That sounds worth keeping in mind.",
        "Safe travels if you head that way.", "I have heard similar talk around the realm.",
        "There should be others interested before long.", "I will keep an eye out while I finish this task.",
        "Sharing the location may help someone nearby.", "A balanced group should handle that well.",
        "Checking your trainer and equipment is always worthwhile.", "Good luck with the next pull.",
    ];

    public static bool TryStartAmbient(GameBot starter, AutonomousBotChat.Context context)
    {
        // True 1.65 Mode: a hired companion speaks as a companion, about what is going on with its player (CompanionChat165)
        if (CompanionChat165.IsCompanion(starter))
            return CompanionChat165.TryAmbient(starter);
        if (!Eligible(starter))
            return false;
        // a bot waiting for a party puts out an /lfg line now and then (veteran-bots review 2026-11-11, rvr-social G6)
        if (AutonomousObjectiveAssignments.IsAwaitingGroupMatchmaking(starter) && TryLfgLine(starter))
            return true;

        bool factionAudience = HasFactionAudience(starter.Realm);
        bool localAudience = HasLocalAudience(starter);
        if (!factionAudience && !localAudience)
            return false;

        bool faction = factionAudience && (!localAudience || Random.Shared.NextDouble() < 0.24);
        lock (Sync)
        {
            if (faction)
            {
                if (LastFactionAmbient.TryGetValue(starter.Realm, out long last) && GameLoop.GameLoopTime - last < 180_000)
                    return false;
                LastFactionAmbient[starter.Realm] = GameLoop.GameLoopTime;
            }
            else
            {
                // per zone, quicker where many bots are (veteran-bots review 2026-11-11, rvr-social G14: a capital of 300
                // bots was as quiet as an empty zone under one line per 90 s per region)
                ushort zone = starter.CurrentZone?.ID ?? starter.CurrentRegionID;
                if (LastLocalAmbient.TryGetValue(zone, out long last) &&
                    GameLoop.GameLoopTime - last < LocalIntervalMilliseconds(BotsInZone(zone, GameLoop.GameLoopTime)))
                    return false;
                LastLocalAmbient[zone] = GameLoop.GameLoopTime;
            }
        }

        // banter165 lines first when one fits what the bot is doing; otherwise today's lines (owner 2026-11-10)
        string opening = BotBanter165.TryOpening(starter, context, faction ? "realm" : "say", out BotBanter165.Topic topic) ??
                         BuildAmbientOpening(starter, context, faction);
        // a realm-wide line goes on its period channel (BotChatChannels165): RvR on the chat group, trade and general
        // talk on /broadcast in the speaker's region, questions on /advice, LFG on /lfg
        BotChatChannels165.Channel channel = faction
            ? BotChatChannels165.Classify(topic?.Situation, opening, IsPvpContext(starter))
            : BotChatChannels165.Channel.Faction;
        if (faction && !BotChatChannels165.Send(channel, starter, opening))
        {
            // nobody of the realm in its region hears a /broadcast: it says it to whoever stands by, or keeps quiet
            if (!localAudience || !starter.Say(opening))
                return false;
            faction = false;
        }
        else if (!faction && !starter.Say(opening))
            return false;

        ScheduleBotReplies(starter, opening, faction, false, topic, channel);
        return true;
    }

    private sealed class LfgBox
    {
        public long Next;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameBot, LfgBox> NextLfg = new();
    private static readonly Dictionary<eRealm, long> LastLfg = new();

    /// <summary>One /lfg line per realm per 40 seconds at most.</summary>
    public const long LfgRealmIntervalMilliseconds = 40_000;

    /// <summary>
    /// A bot waiting for a party (GroupPvE matchmaking) says one /lfg line every 3-6 minutes, as a 2003 player ran his
    /// /lfg macro outside the dungeon, at most one line per realm per 40 seconds and only while a player of the realm is
    /// online. A player who /sends or invites it is answered by the existing invite handling.
    /// </summary>
    private static bool TryLfgLine(GameBot bot)
    {
        if (!BotChatChannels165.PeriodChannels || !HasFactionAudience(bot.Realm))
            return false;
        long now = GameLoop.GameLoopTime;
        LfgBox box = NextLfg.GetValue(bot, _ => new LfgBox { Next = now + Random.Shared.Next(60_000, 180_000) });
        if (now < box.Next)
            return false;
        lock (Sync)
        {
            if (LastLfg.TryGetValue(bot.Realm, out long last) && now - last < LfgRealmIntervalMilliseconds)
                return false;
            LastLfg[bot.Realm] = now;
        }
        box.Next = now + Random.Shared.Next(180_000, 360_001);
        string line = BotBanter165.TryOpener(bot, "lfg", "realm") ??
                      BuiltInLfgLine(bot.Level, bot.CharacterClass?.Name ?? bot.ClassName, BotPartyRoleOf(bot), bot.CurrentZone?.Description, Random.Shared);
        return BotChatChannels165.Send(BotChatChannels165.Channel.Lfg, bot, line);
    }

    private static string BotPartyRoleOf(GameBot bot) => bot.CharacterClass?.ID switch
    {
        (int)eCharacterClass.Cleric or (int)eCharacterClass.Friar or (int)eCharacterClass.Healer or (int)eCharacterClass.Shaman or
            (int)eCharacterClass.Druid or (int)eCharacterClass.Bard or (int)eCharacterClass.Warden => "healer",
        (int)eCharacterClass.Armsman or (int)eCharacterClass.Paladin or (int)eCharacterClass.Warrior or (int)eCharacterClass.Thane or
            (int)eCharacterClass.Hero or (int)eCharacterClass.Champion => "tank",
        (int)eCharacterClass.Sorcerer or (int)eCharacterClass.Enchanter or (int)eCharacterClass.Minstrel or
            (int)eCharacterClass.Skald or (int)eCharacterClass.Mentalist => "cc",
        _ => "dps",
    };

    /// <summary>A period /lfg macro line when the banter library has none for the bot.</summary>
    public static string BuiltInLfgLine(int level, string className, string role, string zone, Random random)
    {
        random ??= Random.Shared;
        string where = string.IsNullOrWhiteSpace(zone) ? string.Empty : $" around {zone}";
        return (role, random.Next(4)) switch
        {
            ("healer", 0) => "healer lf xp grp",
            ("healer", 1) => $"lvl {level} {className} lfg, can heal",
            ("tank", 0) => $"{className} lf xp grp, can tank",
            ("tank", 1) => $"lvl {level} tank lfg{where}",
            ("cc", 0) => $"{className} lfg, have mez",
            (_, 2) => $"lf xp grp{where}",
            (_, 3) => $"{level} {className} lfg, any grp",
            _ => $"lfg {level} {className}, ready now",
        };
    }

    private static readonly Dictionary<eRealm, long> LastAdviceAnswer = new();

    /// <summary>The lowest level of a gamebot that answers on /advice.</summary>
    public const int AdviceAnswerLevel = 20;

    /// <summary>
    /// A player asked on /advice (veteran-bots review 2026-11-11, rvr-social G12): when the bots really know the answer
    /// (a place, a stable master, a spell, the war - AutonomousChatKnowledge), the most seasoned of a sample of the realm's
    /// gamebots of level 20 or more answers on /advice a moment later, as a 2003 advisor did. At most one answer per realm
    /// per 12 seconds; nothing is said when nobody knows.
    /// </summary>
    public static void OnPlayerAdvice(GamePlayer player, string message)
    {
        if (player == null || string.IsNullOrWhiteSpace(message) || !BotChatChannels165.PeriodChannels ||
            !AutonomousChatKnowledge.TryAnswer(player, message, out string answer) || string.IsNullOrWhiteSpace(answer))
            return;
        lock (Sync)
        {
            if (LastAdviceAnswer.TryGetValue(player.Realm, out long last) && GameLoop.GameLoopTime - last < 12_000)
                return;
            LastAdviceAnswer[player.Realm] = GameLoop.GameLoopTime;
        }
        GameBot advisor = SampleResponders(AutonomousBotRegistry.Snapshot(),
                bot => Eligible(bot) && bot.Realm == player.Realm && bot.Level >= AdviceAnswerLevel, message, out _)
            .OrderByDescending(bot => bot.Level).FirstOrDefault();
        if (advisor == null)
            return;
        Schedule(advisor, 2_000 + Random.Shared.Next(2_000), () => BotChatChannels165.Send(BotChatChannels165.Channel.Advice, advisor, answer));
    }

    public static void OnPlayerFactionChat(GamePlayer player, string message)
    {
        if (player == null || string.IsNullOrWhiteSpace(message))
            return;
        BroadcastFaction(player.Name, player.Realm, message);
        lock (Sync)
        {
            if (LastFactionPlayerResponse.TryGetValue(player.Realm, out long last) && GameLoop.GameLoopTime - last < 12_000)
                return;
            LastFactionPlayerResponse[player.Realm] = GameLoop.GameLoopTime;
        }
        SchedulePlayerReplies(player, message, true);
    }

    public static void OnPlayerLocalChat(GamePlayer player, string message)
    {
        if (player == null || string.IsNullOrWhiteSpace(message) || player.CurrentRegion == null)
            return;
        CompanionChat165.OnPlayerSay(player, message);
        // "ding", "ty", "rez pls" read as out-of-world to the intent model; bots may answer those, but never a
        // hard out-of-world word ("the epic football game") (owner 2026-11-10)
        if (AutonomousChatIntentModel.Predict(message) == eAutonomousChatIntent.IgnoreOutOfWorld &&
            !(BanterLibrary165.PeriodWordRescues(message) && BotBanter165.Current.Count > 0))
            return;
        lock (Sync)
        {
            if (LastLocalPlayerResponse.TryGetValue(player.CurrentRegionID, out long last) && GameLoop.GameLoopTime - last < 10_000)
                return;
            LastLocalPlayerResponse[player.CurrentRegionID] = GameLoop.GameLoopTime;
        }
        SchedulePlayerReplies(player, message, false);
    }

    public static bool TryAdvertiseExchangeItem(GameBot seller, string exactItemName)
    {
        if (!Eligible(seller) || string.IsNullOrWhiteSpace(exactItemName) || !HasFactionAudience(seller.Realm))
            return false;
        lock (Sync)
        {
            if (LastFactionAmbient.TryGetValue(seller.Realm, out long last) && GameLoop.GameLoopTime - last < 180_000)
                return false;
            LastFactionAmbient[seller.Realm] = GameLoop.GameLoopTime;
        }

        string item = exactItemName.Trim();
        string opening = Random.Shared.Next(2) == 0
            ? $"Does anybody want to make me an offer on {item}?"
            : $"How much do u think {item} is worth?";
        // trade talk is /broadcast in the seller's region (BotChatChannels165)
        BotChatChannels165.Channel channel = BotChatChannels165.PeriodChannels ? BotChatChannels165.Channel.Trade : BotChatChannels165.Channel.Faction;
        if (!BotChatChannels165.Send(channel, seller, opening))
            return false;
        // repliers may use banter165 "trading" reply lines about this item (owner 2026-11-10)
        var topic = new BotBanter165.Topic { Situation = "trading" };
        topic.Shared["item"] = item;
        ScheduleBotReplies(seller, opening, true, false, topic, channel);
        return true;
    }

    public static string GenerateFactionReply(string incoming, Random random = null)
    {
        random ??= Random.Shared;
        if (incoming.Contains("offer on", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains("how much", StringComparison.OrdinalIgnoreCase) && incoming.Contains("worth", StringComparison.OrdinalIgnoreCase))
        {
            return random.Next(5) switch
            {
                0 => "Compare its level and bonuses before setting the price.",
                1 => "The Realm Exchange should show what similar gear is worth.",
                2 => "That may be useful to someone leveling nearby.",
                3 => "A merchant price can provide a reasonable minimum value.",
                _ => "I might offer a few silver if it suits my class.",
            };
        }
        if (incoming.Contains("mythic", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains(" broken", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains(" op", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains("best class", StringComparison.OrdinalIgnoreCase))
        {
            return random.Next(8) switch
            {
                0 => "Every class has strengths that show in the right group.",
                1 => "Which specialization are you considering?",
                2 => "That ability may work better with the right positioning.",
                3 => "It is worth asking someone who plays that class often.",
                4 => "Equipment and specialization can change how the class feels.",
                5 => "I would compare how it performs solo and in a group.",
                6 => "A careful pull usually tells you more than the delve text.",
                _ => "Try the class and see which playstyle suits you.",
            };
        }
        if (incoming.Contains("lag", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains(" dc", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains("disconnect", StringComparison.OrdinalIgnoreCase))
        {
            return random.Next(5) switch
            {
                0 => "Movement seems steady where I am.",
                1 => "I noticed a short delay too; it seems better now.",
                2 => "Give the area a moment to finish loading.",
                3 => "Hopefully it clears before your next pull.",
                _ => "Stay with your group until movement catches up.",
            };
        }
        return AutonomousChatIntentModel.Predict(incoming) switch
        {
            eAutonomousChatIntent.Greeting => random.Next(2) == 0 ? "Greetings. Good hunting out there." : "Hello there. The realm is awake today.",
            eAutonomousChatIntent.Banter => random.Next(2) == 0 ? "Good luck on your next pull." : "Save some of that energy for the frontier.",
            eAutonomousChatIntent.Abuse => random.Next(3) switch { 0 => "Let us keep faction chat helpful.", 1 => "I would rather focus on the realm.", _ => "Good hunting when you head back out." },
            eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Help => random.Next(2) == 0 ? "Share your level and location so nearby adventurers can help." : "I may be free once I finish my current task.",
            eAutonomousChatIntent.Travel => random.Next(2) == 0 ? "A stable master may shorten the trip." : "Check the road signs and travel with your group.",
            eAutonomousChatIntent.RvR => random.Next(2) == 0 ? "I have heard the frontier has some movement." : "A few defenders may head out before long.",
            eAutonomousChatIntent.Trading => random.Next(2) == 0 ? "The Realm Exchange may have something useful." : "I have seen a little trade around the capital.",
            eAutonomousChatIntent.Dungeon => "A careful group should clear one pull at a time on the way inside.",
            eAutonomousChatIntent.Grinding => "Share the camp and level if you are looking for company.",
            eAutonomousChatIntent.Farewell => "Safe travels. Until next time.",
            eAutonomousChatIntent.IgnoreOutOfWorld => "Let us keep faction chat focused on the realm.",
            _ => FactionReplies[random.Next(FactionReplies.Length)],
        };
    }

    public static string GenerateLocalReply(GameBot bot, string incoming, Random random = null)
    {
        random ??= Random.Shared;
        string activity = Clean(bot?.PersistentRecord?.Activity, "taking a short rest");
        string target = Clean(bot?.PersistentRecord?.TargetName, "the creatures nearby");
        string destination = Clean(bot?.PersistentRecord?.TravelDestination, bot?.CurrentZone?.Description ?? "this area");

        return AutonomousChatIntentModel.Predict(incoming) switch
        {
            eAutonomousChatIntent.Greeting => random.Next(2) == 0 ? $"Hello. I am keeping an eye on {target}." : $"Greetings. I am {LowerFirst(activity)}.",
            eAutonomousChatIntent.Banter => random.Next(2) == 0 ? "Good luck on the next pull." : "Keep your weapon ready and stay with the group.",
            eAutonomousChatIntent.Abuse => random.Next(3) switch { 0 => "Let us keep this helpful.", 1 => "I am focusing on my current task.", _ => "Safe travels when you move on." },
            eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Help => bot?.Group == null ? $"I am working around {target} for now, but I could group." : "I am already moving with a group right now.",
            eAutonomousChatIntent.Travel => $"I am headed toward {destination}.",
            eAutonomousChatIntent.Dungeon => $"We should clear one pull at a time if we head deeper. I am {LowerFirst(activity)}.",
            eAutonomousChatIntent.RvR => "If I head to the frontier, I will stay with the realm group.",
            eAutonomousChatIntent.Trading => "I will check my real inventory and the Realm Exchange when I return to town.",
            eAutonomousChatIntent.Grinding => $"I am focused on {target} at the moment.",
            eAutonomousChatIntent.Farewell => "Safe travels. I will keep working here.",
            eAutonomousChatIntent.IgnoreOutOfWorld => "Let us keep the conversation focused on the realm.",
            _ => random.Next(3) switch
            {
                0 => $"I am focused on {target} at the moment.",
                1 => $"Right now I am {LowerFirst(activity)}.",
                _ => $"I will be around {destination} for a while.",
            },
        };
    }

    public static int RollResponseCount(int eligibleCount, Random random = null)
    {
        if (eligibleCount <= 0)
            return 0;
        random ??= Random.Shared;
        return Math.Min(eligibleCount, random.NextDouble() < 0.32 ? 2 : 1);
    }

    public static bool ContainsMention(string message, string name)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(name))
            return false;
        int start = 0;
        while ((start = message.IndexOf(name, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = start + name.Length;
            bool left = start == 0 || !char.IsLetterOrDigit(message[start - 1]);
            bool right = end == message.Length || !char.IsLetterOrDigit(message[end]);
            if (left && right)
                return true;
            start = end;
        }
        return false;
    }

    public static void BroadcastFaction(string speakerName, eRealm realm, string message)
    {
        string safe = (message ?? string.Empty).Trim();
        if (safe.Length == 0)
            return;
        if (safe.Length > 240)
            safe = safe[..240];
        string formatted = $"({speakerName}) <Faction> {safe}";
        foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRealm(realm))
            player.Out.SendMessage(formatted, eChatType.CT_Broadcast, eChatLoc.CL_ChatWindow);
    }

    private static void SchedulePlayerReplies(GamePlayer player, string message, bool faction)
    {
        bool hasKnowledge = AutonomousChatKnowledge.TryAnswer(player, message, out string knowledge);
        eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
        // veteran review Q30: a bot named in the message always stays a candidate; the rest are sampled (about 64) and
        // only those are scored, instead of building a string for and sorting every bot of the realm
        var mentionedList = new List<GameBot>();
        GameBot[] eligible = RankReplyCandidates(AutonomousBotRegistry.Snapshot()
            .Where(Eligible)
            .Where(bot => bot.Realm == player.Realm)
            .Where(bot => faction || bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE))
            .Where(bot =>
            {
                if (!ContainsMention(message, bot.Name))
                    return true;
                mentionedList.Add(bot);
                return false;
            }), message, Random.Shared);
        GameBot[] mentioned = RankReplyCandidates(mentionedList, message, Random.Shared);
        bool directed = mentioned.Length > 0;
        if (directed)
            eligible = mentioned;
        int count = directed || hasKnowledge || intent is eAutonomousChatIntent.IgnoreOutOfWorld or eAutonomousChatIntent.Abuse
            ? Math.Min(1, eligible.Length)
            : RollResponseCount(eligible.Length);
        HashSet<string> scheduledResponses = new(StringComparer.OrdinalIgnoreCase);
        int scheduledIndex = 0;
        // a rescued "ty" / "rez pls" never falls back to the built-in "keep it on the realm" scolding (owner 2026-11-10)
        bool rescued = intent == eAutonomousChatIntent.IgnoreOutOfWorld && BanterLibrary165.PeriodWordRescues(message);
        for (int index = 0; index < count; index++)
        {
            GameBot responder = eligible[index];
            string response = hasKnowledge
                ? knowledge
                : BotBanter165.TryPlayerReply(responder, player, message, faction ? "realm" : "say", directed) ??
                  (rescued ? null
                    : directed
                    ? GenerateDirectedReply(message)
                    : faction ? GenerateFactionReply(message) : GenerateLocalReply(responder, message));
            if (!TryAddDistinctResponse(scheduledResponses, response, out response))
                continue;
            Schedule(responder, 1_300 + scheduledIndex++ * 1_800 + Random.Shared.Next(1_200), () =>
            {
                if (faction && HasFactionAudience(responder.Realm))
                    BroadcastFaction(responder.Name, responder.Realm, response);
                else if (!faction && HasLocalAudience(responder))
                    responder.Say(response);
            });
        }
    }

    private static void ScheduleBotReplies(GameBot starter, string opening, bool faction, bool fromPlayer, BotBanter165.Topic topic = null,
        BotChatChannels165.Channel channelKind = BotChatChannels165.Channel.Faction)
    {
        string channel = faction ? "realm" : "say";
        bool hasKnowledge = AutonomousChatKnowledge.TryAnswer(starter.Realm, opening, out string knowledge);
        GameBot[] eligible = RankReplyCandidates(AutonomousBotRegistry.Snapshot()
            .Where(bot => bot != starter && Eligible(bot) && bot.Realm == starter.Realm)
            .Where(bot => faction || bot.CurrentRegion == starter.CurrentRegion && bot.IsWithinRadius(starter, WorldMgr.SAY_DISTANCE)),
            opening, Random.Shared);
        int count = hasKnowledge ? Math.Min(1, eligible.Length) : RollResponseCount(eligible.Length);
        HashSet<string> scheduledResponses = new(StringComparer.OrdinalIgnoreCase);
        int scheduledCount = 0;
        for (int index = 0; index < count; index++)
        {
            GameBot responder = eligible[index];
            string response = hasKnowledge ? knowledge : BotBanter165.TryReply(responder, topic, channel) ??
                (faction ? GenerateFactionReply(opening) : GenerateLocalReply(responder, opening));
            if (!TryAddDistinctResponse(scheduledResponses, response, out response))
                continue;
            Schedule(responder, 1_700 + scheduledCount++ * 2_100 + Random.Shared.Next(1_300), () =>
            {
                // a reply stays on the opener's channel (a /broadcast in the opener's region)
                if (faction && BotChatChannels165.HasAudience(channelKind, starter.Realm, starter.CurrentRegion))
                    BotChatChannels165.Send(channelKind, responder.Name, responder.Realm, starter.CurrentRegion, response,
                        responder.Level, responder.CharacterClass?.Name);
                else if (!faction && HasLocalAudience(responder))
                    responder.Say(response);
            });
        }

        // A short optional closing line makes this read like a conversation,
        // while the channel cooldown prevents it from becoming a chat loop.
        if (!fromPlayer && !hasKnowledge && scheduledCount > 0 && Random.Shared.NextDouble() < 0.28)
        {
            string closing = BotBanter165.TryClosing(starter, channel) ??
                             (faction ? "Fair enough. Safe travels." : "Understood. I will keep at it.");
            Schedule(starter, 5_500 + scheduledCount * 1_500, () =>
            {
                if (faction && BotChatChannels165.HasAudience(channelKind, starter.Realm, starter.CurrentRegion))
                    BotChatChannels165.Send(channelKind, starter, closing);
                else if (!faction && HasLocalAudience(starter))
                    starter.Say(closing);
            });
        }
    }

    private static void Schedule(GameBot speaker, int delay, Action action)
    {
        _ = new ECSGameTimer(speaker, timer =>
        {
            if (Eligible(speaker) && !speaker.InCombat && !speaker.IsCasting)
                action();
            return 0;
        }, delay);
    }

    public static bool TryAddDistinctResponse(HashSet<string> scheduled, string response, out string normalized)
    {
        normalized = response?.Trim() ?? string.Empty;
        return scheduled != null && normalized.Length > 0 && scheduled.Add(normalized);
    }

    private static bool Eligible(GameBot bot) => bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
        !CompanionChat165.IsCompanion(bot) &&   // hired companions never join the world's talk (CompanionChat165)
        bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && !bot.InCombat && !bot.IsCasting &&
        !bot.IsOnStableMasterRoute && !bot.IsOnHorse;

    /// <summary>The bot is about RvR: its activity, goal or zone is the frontier.</summary>
    private static bool IsPvpContext(GameBot bot)
    {
        string situation = $"{bot.PersistentRecord?.Activity} {bot.PersistentRecord?.CurrentGoal} {bot.CurrentZone?.Description}";
        return situation.Contains("rvr", StringComparison.OrdinalIgnoreCase) ||
               situation.Contains("pvp", StringComparison.OrdinalIgnoreCase) ||
               situation.Contains("frontier", StringComparison.OrdinalIgnoreCase) || bot.CurrentZone?.IsRvR == true;
    }

    private static bool HasFactionAudience(eRealm realm) => ClientService.Instance.GetPlayersOfRealm(realm).Count > 0;

    private static bool HasLocalAudience(GameBot bot) => bot?.CurrentRegion != null &&
        ClientService.Instance.GetPlayersOfRegion(bot.CurrentRegion)
            .Any(player => player.ObjectState == GameObject.eObjectState.Active && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE));

    private static string BuildAmbientOpening(GameBot starter, AutonomousBotChat.Context context, bool faction)
    {
        if (Random.Shared.NextDouble() < 0.18)
        {
            string activity = Clean(starter.PersistentRecord?.Activity, string.Empty);
            string destination = Clean(starter.PersistentRecord?.TravelDestination, string.Empty);
            if (!string.IsNullOrWhiteSpace(destination) &&
                (activity.StartsWith("Walking to", StringComparison.OrdinalIgnoreCase) ||
                 activity.StartsWith("Approaching", StringComparison.OrdinalIgnoreCase)))
                return Random.Shared.Next(2) == 0
                    ? $"Anyone know where {destination} is?"
                    : $"Does anyone know where {destination} is at?";

            string target = Clean(starter.PersistentRecord?.TargetName, string.Empty);
            if (!string.IsNullOrWhiteSpace(target))
                return Random.Shared.Next(3) switch
                {
                    0 => $"Anyone know where {target} is?",
                    1 => $"Does anyone know where {target} is at?",
                    _ => $"Has anybody ever found {target}?",
                };

            if (!string.IsNullOrWhiteSpace(destination))
                return Random.Shared.Next(3) switch
                {
                    0 => $"Anyone know where {destination} is?",
                    1 => $"How do I get to {destination}?",
                    _ => $"Which stable master takes me to {destination}?",
                };
        }

        if (faction)
        {
            string situation = $"{starter.PersistentRecord?.Activity} {starter.PersistentRecord?.CurrentGoal} {starter.CurrentZone?.Description}";
            bool pvp = situation.Contains("rvr", StringComparison.OrdinalIgnoreCase) ||
                       situation.Contains("pvp", StringComparison.OrdinalIgnoreCase) ||
                       situation.Contains("frontier", StringComparison.OrdinalIgnoreCase) || starter.CurrentZone?.IsRvR == true;
            return BuildFactionTaskOpening(starter, context, pvp);
        }
        return AutonomousBotChat.Generate(context);
    }

    private static string BuildFactionTaskOpening(GameBot starter, AutonomousBotChat.Context context, bool pvp)
    {
        string className = Clean(context.ClassName, starter.ClassName);
        string zone = Clean(context.ZoneName, starter.CurrentZone?.Description ?? "my current area");
        string target = Clean(context.MonsterName, string.Empty);
        string destination = Clean(starter.PersistentRecord?.TravelDestination, string.Empty);
        int choice = Random.Shared.Next(10);

        if (pvp)
        {
            return (choice % 4) switch
            {
                0 => $"I am watching the frontier around {zone}. Where are defenders gathering?",
                1 => $"Any {className}s have advice for supporting a frontier group?",
                2 => "If anyone sees enemy movement, share the location so the realm can respond.",
                _ => "Good luck to everyone heading into the frontier. Stay with your group.",
            };
        }

        if (!string.IsNullOrWhiteSpace(target) && choice < 4)
        {
            return choice switch
            {
                0 => $"I am hunting {target} around {zone}. Has anyone found a steady camp nearby?",
                1 => $"These {target} are keeping me busy in {zone}. Anyone else working nearby?",
                2 => starter.Group?.MemberCount > 1
                    ? $"Our group is taking careful pulls of {target} in {zone}."
                    : $"I am taking careful pulls of {target} in {zone}.",
                _ => $"Has anyone learned a reliable way to handle {target} around {zone}?",
            };
        }

        if (!string.IsNullOrWhiteSpace(destination) && choice < 7)
        {
            return (choice % 3) switch
            {
                0 => $"I am heading toward {destination}. Any advice on the safest route?",
                1 => $"Does anyone know which stable master is best for reaching {destination}?",
                _ => $"Safe travels to anyone heading toward {destination}.",
            };
        }

        return choice switch
        {
            7 => $"Any other {className}s have advice for adventurers around level {starter.Level}?",
            8 => $"How are the hunting grounds around {zone} treating everyone?",
            9 when starter.Group?.MemberCount > 1 => $"Our group is working around {zone}. Are any other parties nearby?",
            _ => FactionOpeners[Random.Shared.Next(FactionOpeners.Length)],
        };
    }

    /// <summary>Reply candidates scored at most (veteran review Q30): a uniform reservoir sample of the eligible bots.</summary>
    public const int ReplyCandidateSample = 64;

    /// <summary>
    /// The eligible candidates to answer, most relevant first (ties in random order). At most
    /// <see cref="ReplyCandidateSample"/> are drawn uniformly (reservoir) and only those are scored; a reply needs one or
    /// two speakers, so a sample this size still finds a bot whose work matches the message. The message is split once.
    /// </summary>
    public static T[] RankReplyCandidates<T>(IEnumerable<T> eligible, string incoming, Random random,
        Func<T, string> describe, int sample = ReplyCandidateSample)
    {
        random ??= Random.Shared;
        var picked = new List<T>(Math.Min(sample, 16));
        int seen = 0;
        foreach (T candidate in eligible)
        {
            seen++;
            if (picked.Count < sample)
                picked.Add(candidate);
            else
            {
                int slot = random.Next(seen);
                if (slot < sample)
                    picked[slot] = candidate;
            }
        }
        if (picked.Count <= 1)
            return picked.ToArray();
        string[] words = RelevanceWords(incoming);
        return picked
            .Select(candidate => (candidate, score: words.Length == 0 ? 0 : RelevanceScore(describe(candidate), words), tie: random.Next()))
            .OrderByDescending(entry => entry.score)
            .ThenBy(entry => entry.tie)
            .Select(entry => entry.candidate)
            .ToArray();
    }

    private static GameBot[] RankReplyCandidates(IEnumerable<GameBot> eligible, string incoming, Random random) =>
        RankReplyCandidates(eligible, incoming, random, bot =>
            $"{bot.PersistentRecord?.Activity} {bot.PersistentRecord?.CurrentGoal} {bot.PersistentRecord?.TargetName} {bot.PersistentRecord?.TravelDestination}");

    /// <summary>The words of a message that count for relevance (four letters or more, punctuation trimmed).</summary>
    public static string[] RelevanceWords(string incoming) =>
        (incoming ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim('?', '!', '.', ',', '\'', '"'))
            .Where(word => word.Length >= 4)
            .ToArray();

    /// <summary>How many of the message's words appear in what the bot is doing.</summary>
    public static int RelevanceScore(string haystack, string[] words)
    {
        if (string.IsNullOrEmpty(haystack))
            return 0;
        int count = 0;
        foreach (string word in words)
            if (haystack.Contains(word, StringComparison.OrdinalIgnoreCase))
                count++;
        return count;
    }

    private static string GenerateDirectedReply(string incoming)
    {
        if (incoming.Contains("stop", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains("quiet", StringComparison.OrdinalIgnoreCase) ||
            incoming.Contains("shut", StringComparison.OrdinalIgnoreCase))
        {
            return Random.Shared.Next(4) switch
            {
                0 => "Understood. I will focus on my task.",
                1 => "All right. Safe travels.",
                2 => "No problem. I will keep faction chat clear.",
                _ => "Okay. I will get back to work.",
            };
        }
        return AutonomousChatIntentModel.Predict(incoming) switch
        {
            eAutonomousChatIntent.Greeting => "Greetings. How can I help?",
            eAutonomousChatIntent.Abuse => "Let us keep the conversation helpful.",
            eAutonomousChatIntent.IgnoreOutOfWorld => "Let us keep the conversation focused on the realm.",
            _ => Random.Shared.Next(3) switch { 0 => "What did you need?", 1 => "Perhaps after this task.", _ => "Yes?" },
        };
    }

    private static string Clean(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().TrimEnd('.', '!', '?');
    private static string LowerFirst(string value) => string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
