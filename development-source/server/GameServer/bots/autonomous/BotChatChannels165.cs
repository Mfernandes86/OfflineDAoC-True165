using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Period chat channels for gamebot talk (veteran-bots review 2026-11-11, M10 / rvr-social G1). Every bot line that
    /// went realm-wide used the invented "&lt;Faction&gt;" channel; in 1.65 there was no realm-wide general channel, and a
    /// player watched these instead (FreddysHouse archive: alliance and chat-group alarms, /broadcast trade at a busy forge,
    /// the /advice channel, the /lfg macro; Prima's Thane tip "Use /chat to plan RvR battle"):
    /// - RvR (sieges, defense calls, scout reports, herald notices, rally answers): the realm's chat group, "[Chat] Name: "..."";
    /// - Trade (exchange adverts, trading talk) and other general talk: /broadcast to the players of the realm in the
    ///   speaker's region, "[Broadcast] Name: ...";
    /// - questions (where is, how do I get to): the advice channel, "[ADVICE ALB] Name: ...";
    /// - LFG: "[LFG] Name (level, class): ...", the server's own /lfg format.
    /// The owner's /faction stays: a player's /faction line and the bots' answers to it keep that channel. Same lines,
    /// same rate limits, another packet type - no extra work. Switch off bot_chat_period_channels for the old
    /// all-on-&lt;Faction&gt; behaviour.
    /// </summary>
    public static class BotChatChannels165
    {
        [ServerProperty("bots", "bot_chat_period_channels",
            "Do gamebots talk on the period channels (RvR on the realm chat group, trade and general talk on /broadcast in the speaker's region, questions on /advice, LFG on /lfg) instead of everything on <Faction>? A player's /faction line is still answered on /faction.", true)]
        public static bool PeriodChannels = true;

        public enum Channel { Faction, RvR, Trade, Advice, Lfg, General }

        /// <summary>The channel for a realm-wide bot line: banter situation first, then what the text is about.</summary>
        public static Channel Classify(string situation, string text, bool pvpContext)
        {
            if (!PeriodChannels)
                return Channel.Faction;
            string s = situation ?? string.Empty;
            if (s.StartsWith("rvr", StringComparison.OrdinalIgnoreCase) || s.Contains("siege", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("relic", StringComparison.OrdinalIgnoreCase) || s.Contains("keep", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("rally", StringComparison.OrdinalIgnoreCase) || s.StartsWith("frontier", StringComparison.OrdinalIgnoreCase))
                return Channel.RvR;
            if (s.StartsWith("trad", StringComparison.OrdinalIgnoreCase) || s.Contains("exchange", StringComparison.OrdinalIgnoreCase))
                return Channel.Trade;
            if (s.StartsWith("lfg", StringComparison.OrdinalIgnoreCase) || s.Contains("group_wanted", StringComparison.OrdinalIgnoreCase))
                return Channel.Lfg;
            eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(text ?? string.Empty);
            if (pvpContext || intent == eAutonomousChatIntent.RvR)
                return Channel.RvR;
            if (intent == eAutonomousChatIntent.Trading)
                return Channel.Trade;
            if (intent == eAutonomousChatIntent.Grouping)
                return Channel.Lfg;
            if (IsQuestion(text) && intent is eAutonomousChatIntent.Travel or eAutonomousChatIntent.Help or eAutonomousChatIntent.Dungeon)
                return Channel.Advice;
            return Channel.General;
        }

        private static bool IsQuestion(string text) => !string.IsNullOrWhiteSpace(text) && text.TrimEnd().EndsWith('?');

        public static eChatType TypeOf(Channel channel) => channel switch
        {
            Channel.RvR => eChatType.CT_Chat,
            Channel.Trade or Channel.General => eChatType.CT_Broadcast,
            Channel.Advice => eChatType.CT_Advise,
            Channel.Lfg => eChatType.CT_LFG,
            _ => eChatType.CT_Broadcast,
        };

        public static string RealmTag(eRealm realm) => realm switch
        {
            eRealm.Albion => "ALB",
            eRealm.Midgard => "MID",
            eRealm.Hibernia => "HIB",
            _ => "NONE",
        };

        /// <summary>The line as the server's own command for that channel prints it.</summary>
        public static string Format(Channel channel, string speaker, eRealm realm, string text, int level = 0, string className = null)
        {
            string safe = (text ?? string.Empty).Trim();
            if (safe.Length > 240)
                safe = safe[..240];
            return channel switch
            {
                Channel.RvR => $"[Chat] {speaker}: \"{safe}\"",
                Channel.Trade or Channel.General => $"[Broadcast] {speaker}: {safe}",
                Channel.Advice => $"[ADVICE {RealmTag(realm)}] {speaker}: {safe}",
                Channel.Lfg => level > 0 && !string.IsNullOrWhiteSpace(className)
                    ? $"[LFG] {speaker} ({level}, {className}): {safe}"
                    : $"[LFG] {speaker}: {safe}",
                _ => $"({speaker}) <Faction> {safe}",
            };
        }

        /// <summary>Whether the channel reaches a player only in the speaker's region (/broadcast).</summary>
        public static bool IsRegional(Channel channel) => channel is Channel.Trade or Channel.General;

        /// <summary>The players a line on this channel reaches.</summary>
        public static IEnumerable<GamePlayer> Audience(Channel channel, eRealm realm, Region region)
        {
            if (IsRegional(channel))
                return region == null
                    ? Enumerable.Empty<GamePlayer>()
                    : ClientService.Instance.GetPlayersOfRegion(region).Where(p => p.Realm == realm);
            return ClientService.Instance.GetPlayersOfRealm(realm);
        }

        public static bool HasAudience(Channel channel, eRealm realm, Region region) => Audience(channel, realm, region).Any();

        /// <summary>
        /// Sends a bot's line on a channel. The Faction channel keeps its old form; with period channels off every line
        /// goes there. False when nobody would hear it (a /broadcast with no player of the realm in the region).
        /// </summary>
        public static bool Send(Channel channel, string speaker, eRealm realm, Region region, string text, int level = 0, string className = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (!PeriodChannels || channel == Channel.Faction)
            {
                AutonomousBotChatCoordinator.BroadcastFaction(speaker, realm, text);
                return true;
            }
            string line = Format(channel, speaker, realm, text, level, className);
            eChatType type = TypeOf(channel);
            bool any = false;
            foreach (GamePlayer player in Audience(channel, realm, region))
            {
                player.Out.SendMessage(line, type, eChatLoc.CL_ChatWindow);
                any = true;
            }
            if (any)
                FeatureCounters165.Add($"bot_chat_{channel.ToString().ToLowerInvariant()}");
            return any;
        }

        /// <summary>A gamebot's line on a channel (its region for /broadcast, its level and class for /lfg).</summary>
        public static bool Send(Channel channel, GameBot speaker, string text) =>
            speaker != null && Send(channel, speaker.Name, speaker.Realm, speaker.CurrentRegion, text, speaker.Level, speaker.CharacterClass?.Name);
    }
}
