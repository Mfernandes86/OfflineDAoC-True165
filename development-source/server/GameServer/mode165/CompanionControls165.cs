using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Companion controls and AI rows from the veteran review (M7, owner 2026-11-11) and the owner's "Twist songs /
    /// chants" option. Every reader goes through <see cref="CompanionOptions165.Choice"/>, which is always 0 (Automatic)
    /// for a gamebot, so gamebots keep their behaviour; the rules themselves are pure and unit tested.
    /// Generic option kinds 40 and up (CompanionOptions165): 40 twist, 41 stance, 42 crowd control, 43 pet, 44 weapon,
    /// 45 Warden chant, 46 Warden role, 47 Stag, 48 Critical Shot, 49 opening stun, 50 Engage, 51 shield swap for Slam.
    /// </summary>
    public static class CompanionControls165
    {
        // ---- Twist songs / chants (owner 2026-11-11: default OFF, companions only)

        /// <summary>The "Twist songs / chants" row: 0 Automatic (hold one song, as since 2026-10-30), 1 twist.</summary>
        public const int TwistOn = 1;

        /// <summary>The classes whose songs or chants pulse and may be twisted: Bard, Minstrel, Skald (songs) and Paladin
        /// (chants). The Warden is left out: its pulsing bladeturn's recast can outlast the bubble it gives, so a twist
        /// away and back loses the bladeturn (the old twisting code never cycled it either).</summary>
        public static bool TwistClass(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Bard or eCharacterClass.Minstrel or eCharacterClass.Skald or eCharacterClass.Paladin;

        /// <summary>Pure: the row is offered to a twisting class that knows two or more kinds of song or chant.</summary>
        public static bool OffersTwist(eCharacterClass characterClass, int songKinds) => TwistClass(characterClass) && songKinds >= 2;

        /// <summary>The kinds of song or chant this companion holds now (one per kind).</summary>
        public static int SongKinds(GameBot bot)
        {
            if (bot?.CharacterClass == null)
                return 0;
            bool paladin = bot.CharacterClass.ID == (int)eCharacterClass.Paladin;
            return (bot.MiscSpells ?? []).Concat(bot.InstantMiscSpells ?? [])
                .Where(spell => spell != null && spell.Level <= bot.Level && spell.IsPulsing && !spell.IsHarmful &&
                                (paladin ? BotSongTwistPolicy.IsManagedSong(bot, spell) : BotBrain.IsMaintainableClassBuff(spell)))
                .Select(spell => spell.SpellType).Distinct().Count();
        }

        /// <summary>Whether this companion twists its songs or chants (always false for gamebots).</summary>
        public static bool TwistsSongs(GameBot bot) =>
            bot?.CharacterClass != null && TwistClass((eCharacterClass)bot.CharacterClass.ID) &&
            CompanionOptions165.Choice(bot, "twist") == TwistOn;

        /// <summary>
        /// Pure: may a song join the twist next to the held one? Travelling only the travel songs (speed, and the Bard's
        /// endurance or a Skald's or Minstrel's health song, as the old twisting code allowed); otherwise any song but
        /// speed. An unaffordable or the held song itself never joins.
        /// </summary>
        public static bool TwistPartner(eCharacterClass characterClass, eSpellType song, eSpellType held, bool traveling,
            bool affordable) =>
            affordable && song != held &&
            (traveling ? BotSongTwistPolicy.IsCompanionTravelSong(characterClass, song) : song != eSpellType.SpeedEnhancement);

        /// <summary>
        /// Pure: a Paladin chant joins the twist - every chant but the combat heal, which only joins while someone near is
        /// hurt in a fight (the old twisting code's rule).
        /// </summary>
        public static bool PaladinTwistPartner(eSpellType chant, eSpellType held, bool combatHealing, bool affordable) =>
            affordable && chant != held && (chant != eSpellType.CombatHeal || combatHealing);

        /// <summary>How long a twisting companion's song switch plays without its sound: the cast plus this margin.</summary>
        public const int TwistQuietMarginMilliseconds = 2_000;

        /// <summary>Pure: a song switch is silent (a twisting companion going from one song to the next); the first song
        /// of a set plays its sound as always.</summary>
        public static bool QuietSwitch(bool twisting, bool anotherSongPlaying) => twisting && anotherSongPlaying;

        // ---- /passive keeps the lifesavers (owner decision O2, 2026-11-11)

        /// <summary>How a /passive companion may heal this turn: not at all (not passive), instant heals and cure mez only
        /// (its player is moving), or its normal heals (its player has stopped).</summary>
        public enum PassiveHealing : byte { None = 0, WhileMoving = 1, Stopped = 2 }

        /// <summary>
        /// Pure (O2): a heal a /passive companion may cast - an instant heal or a cure mez any time, a cast-time heal only
        /// while its player stands still (it never stops and falls behind a moving player).
        /// </summary>
        public static bool PassiveAllowsHeal(PassiveHealing healing, bool instant, bool cureMez) =>
            healing == PassiveHealing.Stopped || healing == PassiveHealing.WhileMoving && (instant || cureMez);

        /// <summary>
        /// Pure (O2): a cast /passive lets finish - a charm (a charmed pet must not turn), the speed song or chant, a cure
        /// mez, an instant heal, or a cast-time heal while the player stands still. Everything else stops (no fighting, no
        /// long casts).
        /// </summary>
        public static bool PassiveKeepsCast(bool charm, bool speedSong, bool cureMez, bool heal, bool instant, bool leaderMoving) =>
            charm || speedSong || cureMez || heal && (instant || !leaderMoving);

        // ---- Per-companion stance (M7, G13)

        /// <summary>The "Stance" row's choices: 0 Automatic (the group's stance), 1 aggressive, 2 defensive, 3 passive.</summary>
        public const int StanceGroup = 0, StanceAggressive = 1, StanceDefensive = 2, StancePassive = 3;

        /// <summary>Pure: the stance this companion's own choice sets, or null to follow the group's.</summary>
        public static CompanionStance? OwnStance(int choice) => choice switch
        {
            StanceAggressive => CompanionStance.Aggressive,
            StanceDefensive => CompanionStance.Defensive,
            StancePassive => CompanionStance.Passive,
            _ => null,
        };

        /// <summary>Pure: a companion's stance - its own choice when it has one, else its player's /aggressive, /defensive
        /// or /passive.</summary>
        public static CompanionStance EffectiveStance(int ownChoice, CompanionStance groupStance) =>
            OwnStance(ownChoice) ?? groupStance;
    }
}
