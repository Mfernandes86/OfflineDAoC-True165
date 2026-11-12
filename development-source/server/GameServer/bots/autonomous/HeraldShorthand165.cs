using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Herald notices in veteran shorthand (veteran-bots review 2026-11-11, M10 / rvr-social G5). Siege notices read as
    /// formal prose ("Sharpen your blades for X!", "Warbands are marching on X. Join the assault as you arrive!"); a 2003
    /// realm called "Ben under attack, ~40 albs at the outer door, need ppl" or "rams on the inner at Crau": short,
    /// numeric, with the keep nicknames players used. When a notice belongs to a live siege, the line is rewritten from the
    /// event's own numbers (BattleNotice: who attacks, who defends, how many are there, muster or march); otherwise the
    /// keep names in it are shortened. Text only - the events, their timing and their numbers are untouched. Server
    /// property bot_herald_shorthand (on) keeps the formal prose when switched off.
    /// </summary>
    public static class HeraldShorthand165
    {
        [ServerProperty("bots", "bot_herald_shorthand",
            "Are siege herald notices on the realm chat written in 2003 veteran shorthand (\"Ben under attack, ~20 mids at the door, need ppl\") with keep nicknames, instead of formal prose?", true)]
        public static bool Enabled = true;

        /// <summary>The nicknames players used for the frontier keeps and relic keeps (a word of the keep's name -> nickname).</summary>
        private static readonly (string Word, string Nick)[] Nicknames =
        [
            ("Benowyc", "Ben"), ("Berkstead", "Berk"), ("Boldiam", "Boldi"), ("Erasleigh", "Eras"), ("Hurbury", "Hurb"),
            ("Renaris", "Ren"), ("Sursbrooke", "Surs"), ("Caledon", "Caledon"), ("Excalibur", "Excal"), ("Myrddin", "Myrd"),
            ("Bledmeer", "Bled"), ("Arvakr", "Arv"), ("Blendrake", "Blend"), ("Fensalir", "Fens"), ("Glenlock", "Glen"),
            ("Hlidskialf", "Hlid"), ("Nottmoor", "Notty"), ("Grallarhorn", "Grallar"), ("Mjollner", "Mjoll"),
            ("Crauchon", "Crau"), ("Crimthain", "Crim"), ("Ailinne", "Ailinne"), ("Bolg", "Bolg"), ("Behnn", "Behnn"),
            ("nGed", "nGed"), ("Scathaig", "Scath"), ("Lamfhota", "Lamf"), ("Dagda", "Dagda"),
        ];

        public static string RealmPlural(eRealm realm) => realm switch
        {
            eRealm.Albion => "albs",
            eRealm.Midgard => "mids",
            eRealm.Hibernia => "hibs",
            _ => "enemies",
        };

        /// <summary>A keep's nickname ("Caer Benowyc" -> "Ben"), or its name when players had none for it.</summary>
        public static string Nickname(string place)
        {
            if (string.IsNullOrWhiteSpace(place))
                return place;
            foreach ((string word, string nick) in Nicknames)
                if (place.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return nick;
            return place;
        }

        /// <summary>Every keep name in a text shortened to its nickname ("Caer Benowyc", "Bledmeer Faste" -> "Ben", "Bled").</summary>
        public static string ShortenNames(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            foreach ((string word, string nick) in Nicknames)
                foreach (string full in new[] { $"Caer {word}", $"Castle {word}", $"Dun {word}", $"{word} Faste", word })
                {
                    int at = text.IndexOf(full, StringComparison.OrdinalIgnoreCase);
                    if (at < 0)
                        continue;
                    text = text[..at] + nick + text[(at + full.Length)..];
                    break;
                }
            return text;
        }

        /// <summary>A rough head count as a scout gave it: "a few", "1fg", "2fg", "~25", "zerg".</summary>
        public static string RoughCount(int count) => count switch
        {
            <= 3 => "a few",
            <= 8 => "1fg",
            <= 16 => "2fg",
            <= 40 => $"~{(int)Math.Round(count / 5.0) * 5}",
            _ => "zerg",
        };

        /// <summary>
        /// The notice for this realm in shorthand. siege = the live event's numbers, or null. place = the objective's name.
        /// Null when nothing better can be said (the caller keeps the original text, names shortened).
        /// </summary>
        public static string Rewrite(eRealm listener, string place, AutonomousRvrEventLayer.BattleNotice siege, Random random = null)
        {
            if (siege == null || string.IsNullOrWhiteSpace(place))
                return null;
            random ??= Random.Shared;
            string nick = Nickname(place);
            int pick = random.Next(3);
            if (listener == siege.Defender)
            {
                string them = RealmPlural(siege.Attacker);
                string count = RoughCount(Math.Max(siege.PresentAttackers, siege.Attackers));
                return siege.BattleStarted
                    ? pick switch
                    {
                        0 => $"{nick} under attack, {count} {them} at the door, need ppl",
                        1 => $"{them} hitting {nick}, {count}, all to {nick} pls",
                        _ => $"{nick} doors under attack, {count} {them}, get there",
                    }
                    : pick switch
                    {
                        0 => $"{count} {them} massing for {nick}",
                        1 => $"{them} gathering on {nick}, {count}, heads up",
                        _ => $"{count} {them} heading for {nick}, form up",
                    };
            }
            if (listener == siege.Attacker)
            {
                string ours = RoughCount(Math.Max(siege.PresentAttackers, siege.Attackers));
                if (siege.BattleStarted)
                    return pick switch
                    {
                        0 => $"at {nick} doors, {ours} of us, need more",
                        1 => $"rams on {nick}, come help",
                        _ => $"{nick} door going down, {ours} here, get in",
                    };
                if (siege.MusterRemainingMilliseconds == 0)
                    return pick switch
                    {
                        0 => $"moving on {nick} now, {ours} strong",
                        1 => $"army rolling to {nick}, catch up",
                        _ => $"{nick} raid moving out, {ours}",
                    };
                return pick switch
                {
                    0 => $"forming up for {nick}, {ours} so far, come join",
                    1 => $"{nick} raid forming, need healers and more",
                    _ => $"raid on {nick} soon, get to the siege camp",
                };
            }
            // a third realm watching two others fight
            return $"{RealmPlural(siege.Attacker)} hitting {RealmPlural(siege.Defender)} at {nick}, " +
                   $"{RoughCount(Math.Max(siege.PresentAttackers, siege.Attackers))} vs {RoughCount(Math.Max(siege.PresentDefenders, siege.Defenders))}";
        }

        /// <summary>
        /// RealmEventNotices: the line actually sent for a notice. Only siege events (rvr-...) are rewritten; a realm raid's
        /// notice and anything after the switch is off stays as it is.
        /// </summary>
        public static string ForNotice(string eventId, eRealm listener, string text)
        {
            if (!Enabled || string.IsNullOrEmpty(eventId) || !eventId.StartsWith("rvr-", StringComparison.Ordinal))
                return text;
            try
            {
                AutonomousRvrEventLayer.BattleNotice siege = AutonomousRvrEventLayer.Snapshot()
                    .FirstOrDefault(n => n.TargetId == eventId);
                int keepId = BanterLibrary165.KeepIdOf(eventId);
                string place = keepId >= 0 ? GameServer.KeepManager?.GetKeepByID(keepId)?.Name : null;
                // an outcome (taken, held, fallen) keeps its own text: only the names are shortened
                bool outcome = text.Contains("fallen", StringComparison.OrdinalIgnoreCase) ||
                               text.Contains("captured", StringComparison.OrdinalIgnoreCase) ||
                               text.Contains("held", StringComparison.OrdinalIgnoreCase) ||
                               text.Contains("standing down", StringComparison.OrdinalIgnoreCase);
                return (!outcome ? Rewrite(listener, place, siege) : null) ?? ShortenNames(text);
            }
            catch (Exception)
            {
                return text;   // a notice is never lost to its rewrite
            }
        }
    }
}
