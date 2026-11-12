using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>
    /// Live RvR answers (veteran-bots review 2026-11-11, M10 / rvr-social G10). A player asking "who has Ben?", "is Crau
    /// under attack?", "where's the action?" or "how many relics do we have?" got a generic "I have heard the frontier has
    /// some movement". A veteran answered from /realm: "mids have Ben and Bled, albs hitting Crau, we have 2 relics".
    /// - A question about one keep (its name or nickname): who holds it and whether it is under attack.
    /// - Any other RvR question: the realm's relics, how many keeps each realm holds on the frontier, and the keeps under
    ///   attack now (live sieges and keeps in combat), worked out at most once a minute per realm and cached.
    /// Cost: a walk over the frontier keeps (about 30-50) and the relics, at most once a minute per realm.
    /// </summary>
    public static class BotRvrKnowledge165
    {
        public const long CacheMilliseconds = 60_000;
        private static readonly ushort[] FrontierRegions = [1, 100, 200];
        private static readonly ConcurrentDictionary<eRealm, (string Answer, long Until)> Cache = new();

        private static readonly string[] RvrWords =
        [
            "who has", "who owns", "who holds", "under attack", "under siege", "being sieged", "relic", "the action",
            "any action", "rvr", "keeps", "siege", "zerg", "frontier", "door down",
        ];

        /// <summary>Whether a line asks about the war (a question with an RvR word in it).</summary>
        public static bool LooksLikeRvrQuestion(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;
            string q = query.Trim();
            bool question = q.EndsWith('?') || q.StartsWith("who ", StringComparison.OrdinalIgnoreCase) ||
                            q.StartsWith("where", StringComparison.OrdinalIgnoreCase) ||
                            q.StartsWith("any ", StringComparison.OrdinalIgnoreCase) ||
                            q.StartsWith("is ", StringComparison.OrdinalIgnoreCase) ||
                            q.StartsWith("how many", StringComparison.OrdinalIgnoreCase);
            // "where is Caer Benowyc?" is a way-finding question (AutonomousChatKnowledge's location answer), not a status one
            return question && (RvrWords.Any(w => q.Contains(w, StringComparison.OrdinalIgnoreCase)) ||
                                MentionedKeepWord(q) != null && !q.StartsWith("where", StringComparison.OrdinalIgnoreCase));
        }

        private static readonly string[] KeepWords =
        [
            "Benowyc", "Berkstead", "Boldiam", "Erasleigh", "Hurbury", "Renaris", "Sursbrooke", "Excalibur", "Myrddin",
            "Bledmeer", "Arvakr", "Blendrake", "Fensalir", "Glenlock", "Hlidskialf", "Nottmoor", "Grallarhorn", "Mjollner",
            "Crauchon", "Crimthain", "Ailinne", "Bolg", "Behnn", "nGed", "Scathaig", "Lamfhota", "Dagda",
        ];

        private static readonly (string Nick, string Word)[] NickWords =
        [
            ("ben", "Benowyc"), ("berk", "Berkstead"), ("boldi", "Boldiam"), ("eras", "Erasleigh"), ("hurb", "Hurbury"),
            ("ren", "Renaris"), ("surs", "Sursbrooke"), ("excal", "Excalibur"), ("myrd", "Myrddin"), ("bled", "Bledmeer"),
            ("arv", "Arvakr"), ("blend", "Blendrake"), ("fens", "Fensalir"), ("glen", "Glenlock"), ("hlid", "Hlidskialf"),
            ("notty", "Nottmoor"), ("grallar", "Grallarhorn"), ("mjoll", "Mjollner"), ("crau", "Crauchon"),
            ("crim", "Crimthain"), ("scath", "Scathaig"), ("lamf", "Lamfhota"),
        ];

        /// <summary>The keep a line names (full word or a player nickname as a whole word), or null.</summary>
        public static string MentionedKeepWord(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;
            foreach (string word in KeepWords)
                if (query.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return word;
            foreach ((string nick, string word) in NickWords)
                if (AutonomousBotChatCoordinator.ContainsMention(query, nick))
                    return word;
            return null;
        }

        /// <summary>One keep's state in shorthand: "mids have Ben, under attack".</summary>
        public static string KeepLine(string keepName, eRealm owner, eRealm asker, bool underAttack)
        {
            string nick = HeraldShorthand165.Nickname(keepName);
            string who = owner == asker ? "we have" : owner == eRealm.None ? "nobody holds" : $"{HeraldShorthand165.RealmPlural(owner)} have";
            return $"{who} {nick}{(underAttack ? ", under attack atm" : string.Empty)}";
        }

        /// <summary>The realm summary line from counted facts.</summary>
        public static string Summary(eRealm asker, int ourRelics, IReadOnlyDictionary<eRealm, int> keepsHeld, IReadOnlyList<string> fights)
        {
            var parts = new List<string>();
            if (fights.Count > 0)
                parts.Add(string.Join(", ", fights.Take(2)));
            if (keepsHeld.Count > 0)
                parts.Add("keeps: " + string.Join(" ", new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
                    .Select(r => $"{HeraldShorthand165.RealmPlural(r)} {keepsHeld.GetValueOrDefault(r)}")));
            parts.Add(ourRelics == 1 ? "we have 1 relic" : $"we have {ourRelics} relics");
            if (fights.Count == 0)
                parts.Add("quiet out there atm");
            return string.Join(", ", parts);
        }

        /// <summary>AutonomousChatKnowledge: a live answer to an RvR question, or false.</summary>
        public static bool TryAnswer(eRealm realm, string query, out string answer)
        {
            answer = string.Empty;
            if (!LooksLikeRvrQuestion(query) || GameServer.KeepManager == null)
                return false;
            try
            {
                string keepWord = MentionedKeepWord(query);
                if (keepWord != null)
                {
                    AbstractGameKeep keep = FrontierKeeps().FirstOrDefault(k => k.Name.Contains(keepWord, StringComparison.OrdinalIgnoreCase));
                    if (keep != null)
                    {
                        answer = KeepLine(keep.Name, keep.Realm, realm, keep.InCombat);
                        return true;
                    }
                }
                long now = GameLoop.GameLoopTime;
                if (Cache.TryGetValue(realm, out var cached) && now < cached.Until)
                {
                    answer = cached.Answer;
                    return true;
                }
                var held = new Dictionary<eRealm, int>();
                var fights = new List<string>();
                foreach (AbstractGameKeep keep in FrontierKeeps())
                {
                    held[keep.Realm] = held.GetValueOrDefault(keep.Realm) + 1;
                    if (keep.InCombat && fights.Count < 3)
                        fights.Add($"{HeraldShorthand165.Nickname(keep.Name)} under attack");
                }
                foreach (AutonomousRvrEventLayer.BattleNotice siege in AutonomousRvrEventLayer.Snapshot())
                {
                    if (fights.Count >= 3 || !siege.BattleStarted)
                        continue;
                    int keepId = BanterLibrary165.KeepIdOf(siege.TargetId);
                    string name = keepId >= 0 ? GameServer.KeepManager.GetKeepByID(keepId)?.Name : null;
                    if (name == null)
                        continue;
                    string line = $"{HeraldShorthand165.RealmPlural(siege.Attacker)} hitting {HeraldShorthand165.Nickname(name)}";
                    if (!fights.Any(f => f.StartsWith(HeraldShorthand165.Nickname(name), StringComparison.Ordinal)))
                        fights.Add(line);
                }
                answer = Summary(realm, RelicMgr.GetRelicCount(realm), held, fights);
                Cache[realm] = (answer, now + CacheMilliseconds);
                return true;
            }
            catch (Exception)
            {
                answer = string.Empty;
                return false;
            }
        }

        private static IEnumerable<AbstractGameKeep> FrontierKeeps()
        {
            foreach (ushort region in FrontierRegions)
            {
                ICollection<AbstractGameKeep> keeps = GameServer.KeepManager.GetKeepsOfRegion(region);
                if (keeps == null)
                    continue;
                foreach (AbstractGameKeep keep in keeps)
                    if (keep is GameKeep && !keep.IsPortalKeep)   // keeps only, not their towers
                        yield return keep;
            }
        }
    }
}
