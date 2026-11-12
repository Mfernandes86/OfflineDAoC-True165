using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Veteran review M11 (2026-11-11): what a 2003 player knew about a hunting spot before walking there, worked out by
    /// the background camp builder once per catalog generation (no work on the game thread, nothing per bot turn):
    /// how tightly the monsters stand (pack factor), how many cast spells, what kind of creature they are and how wide
    /// the spot is. A camp choice reads it with a few multiplies:
    /// <list type="bullet">
    /// <item>a party without crowd control, and a solo bot, draws dense camps less often (adds kill);</item>
    /// <item>pure melee classes draw caster-heavy camps less often (Prima Hero: avoid spell-casting monsters);</item>
    /// <item>a class draws its favourite prey more often (Prima Hunter: animals and insects; Spiritmaster: reptiles and
    /// insects; Nightshade: animals, best for piercing);</item>
    /// <item>solo dungeons depend on the class: pet classes and stealthers a little more often, support classes rarely.</item>
    /// </list>
    /// No con ceiling, combat, XP or coin number changes; only how often an already-legal camp is drawn.
    /// </summary>
    public static class AutonomousCampKnowledge
    {
        [ServerProperty("bots", "bot_camp_knowledge",
            "Veteran review M11 (2026-11-11): camp choices weigh pack density, caster share and creature type (worked out by the background camp builder), and solo dungeon chances follow the class.", true)]
        public static bool Enabled = true;

        /// <summary>Camp traits from the background builder (default: unknown, every factor 1).</summary>
        public readonly record struct Traits(float PackFactor, float CasterShare, byte BodyType, int Radius, int Measured)
        {
            public bool Known => Measured > 0;
        }

        /// <summary>One monster as the builder sees it.</summary>
        public readonly record struct Member(Vector3 Position, int AggroRange, bool Caster, byte BodyType);

        /// <summary>A pack this dense (other monsters within each one's reach, on average) counts as dense.</summary>
        public const float DensePack = 2f;

        /// <summary>The social reach used for a monster that does not attack on sight.</summary>
        public const int MinimumSocialRange = 300;

        /// <summary>
        /// Pure rule: the traits of a camp's monsters around its anchor. Pack factor is the average number of other
        /// members within each member's reach (aggro range, at least <see cref="MinimumSocialRange"/>); caster share the
        /// fraction with harmful spells; body type the most common one (0 when none is a majority of a third); radius the
        /// farthest member from the anchor.
        /// </summary>
        public static Traits Measure(IReadOnlyList<Member> members, Vector3 anchor)
        {
            if (members == null || members.Count == 0)
                return default;
            int neighbours = 0, casters = 0;
            float farthestSquared = 0;
            Span<int> bodies = stackalloc int[16];
            for (int i = 0; i < members.Count; i++)
            {
                Member m = members[i];
                float reach = Math.Max(MinimumSocialRange, m.AggroRange);
                float reachSquared = reach * reach;
                for (int j = 0; j < members.Count; j++)
                    if (i != j && Vector3.DistanceSquared(m.Position, members[j].Position) <= reachSquared)
                        neighbours++;
                if (m.Caster)
                    casters++;
                if (m.BodyType < bodies.Length)
                    bodies[m.BodyType]++;
                float d = Vector2.DistanceSquared(new Vector2(m.Position.X, m.Position.Y), new Vector2(anchor.X, anchor.Y));
                if (d > farthestSquared)
                    farthestSquared = d;
            }
            int bestBody = 0, bestCount = 0;
            for (int b = 1; b < bodies.Length; b++)
                if (bodies[b] > bestCount)
                {
                    bestCount = bodies[b];
                    bestBody = b;
                }
            if (bestCount * 3 < members.Count)
                bestBody = 0;
            return new Traits((float)neighbours / members.Count, (float)casters / members.Count, (byte)bestBody,
                (int)MathF.Sqrt(farthestSquared), members.Count);
        }

        // ---------------------------------------------------------------- class tables (Prima class sections)

        private const byte Animal = 1, Insect = 7, Reptile = 9;

        /// <summary>Pure rule: the creature types this class hunts best (empty for most classes).</summary>
        public static bool IsFavouritePrey(eCharacterClass characterClass, byte bodyType) => characterClass switch
        {
            eCharacterClass.Hunter => bodyType is Animal or Insect,
            eCharacterClass.Spiritmaster => bodyType is Reptile or Insect,
            eCharacterClass.Nightshade => bodyType is Animal,
            _ => false,
        };

        /// <summary>Pure rule: classes with no magic of their own, for whom caster camps are the hard ones.</summary>
        public static bool IsPureMelee(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Armsman or eCharacterClass.Mercenary or eCharacterClass.Warrior or eCharacterClass.Berserker or
            eCharacterClass.Savage or eCharacterClass.Hero or eCharacterClass.Blademaster;

        /// <summary>Pure rule: the draw multiplier of a camp for a solo bot of this class.</summary>
        public static double SoloFactor(eCharacterClass characterClass, Traits traits)
        {
            if (!traits.Known)
                return 1;
            double factor = 1;
            if (traits.BodyType != 0 && IsFavouritePrey(characterClass, traits.BodyType))
                factor *= 1.5;
            bool melee = IsPureMelee(characterClass);
            if (melee && traits.CasterShare > 0.5f)
                factor *= 0.5;
            if (traits.PackFactor >= DensePack)
                factor *= melee ? 0.5 : 0.75;
            return factor;
        }

        /// <summary>Pure rule: the draw multiplier of a camp for a party (dense camps are for parties with crowd control).</summary>
        public static double GroupFactor(bool partyHasCrowdControl, Traits traits) =>
            traits.Known && !partyHasCrowdControl && traits.PackFactor >= DensePack ? 0.5 : 1;

        /// <summary>
        /// Pure rule (permille): how often a solo bot of this class picks a dungeon when both kinds of camp are open. Pet
        /// classes and stealthers 15%, support classes 3% (Prima Healer "really shouldn't solo past level 4"), everyone
        /// else the old 10%.
        /// </summary>
        public static int SoloDungeonPermille(eCharacterClass characterClass) => characterClass switch
        {
            eCharacterClass.Cabalist or eCharacterClass.Theurgist or eCharacterClass.Necromancer or
                eCharacterClass.Enchanter or eCharacterClass.Animist or eCharacterClass.Spiritmaster or
                eCharacterClass.Bonedancer or eCharacterClass.Infiltrator or eCharacterClass.Nightshade or
                eCharacterClass.Shadowblade or eCharacterClass.Scout or eCharacterClass.Ranger or
                eCharacterClass.Hunter => 150,
            eCharacterClass.Cleric or eCharacterClass.Friar or eCharacterClass.Healer or eCharacterClass.Shaman or
                eCharacterClass.Druid or eCharacterClass.Bard => 30,
            _ => AutonomousBotDecisionEngine.SoloDungeonPreferencePermille,
        };
    }
}
