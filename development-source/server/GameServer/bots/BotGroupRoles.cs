using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// M2 (veteran review 2026-11-11): spec-aware group roles, worked out once when a gamebot party forms.
    /// Period groups asked for a shield tank as main tank and puller, a Rejuvenation Cleric or Mending Healer in the healer
    /// slot, and one crowd-control player; a two-hander tank was damage or the off-tank (Prima: one tank is main tank and
    /// puller, the other manages taunts and draws aggro off the CC; "a Cleric who is a lousy healer" exists because of
    /// spec). Before this the party roles knew only the class, so a two-hander Armsman was as likely to main-tank as a
    /// shield Armsman, and a Smite Cleric or a Paladin could be the only healer.
    ///
    /// The ranking is read from the points the bot really spent (its spec template label is only the tie-break at low
    /// levels). The role sheet is a small object per group, published by the group coordinator and read lock-free by the
    /// members' brains: only the main tank sets the kill target, the off-tank owns the peels, a two-hander off-tank does
    /// not taunt the main tank's monster and flanks it instead. Nothing here runs in a fight loop beyond a lookup.
    /// </summary>
    public static class BotGroupRoles
    {
        /// <summary>Healer ranks: lower is better. 0-1 are real healers; 9 cannot heal a group.</summary>
        public const int PrimaryHealer = 0, StrongHealer = 1, BackupHealer = 2, PinchHealer = 3, LastResortHealer = 4, NoHealer = 9;
        /// <summary>Crowd-control ranks: lower is better; 9 is no crowd-control spec.</summary>
        public const int NoCrowdControl = 9;
        /// <summary>A crowd-control slot is named only for parties this size or larger (Freddyshouse: CC is a 5+ job).</summary>
        public const int CrowdControlSlotMinimumSize = 5;

        /// <summary>
        /// The line a bot is specced in: the class line it trained highest, or (all lines still at 1, a fresh bot) the
        /// line its spec template names. Null when the class has none of these lines.
        /// </summary>
        public static string MainLine(eCharacterClass characterClass, Func<string, int> specLevel, eSpecType label)
        {
            string[] lines = LinesOf(characterClass);
            if (lines.Length == 0)
                return null;
            string best = null;
            int bestLevel = 1;
            bool tie = false;
            foreach (string line in lines)
            {
                int level = specLevel?.Invoke(line) ?? 0;
                if (level > bestLevel)
                {
                    best = line;
                    bestLevel = level;
                    tie = false;
                }
                else if (level == bestLevel && best != null)
                    tie = true;
            }
            return best != null && !tie ? best : LineFromLabel(label) ?? best;
        }

        private static string[] LinesOf(eCharacterClass characterClass) => characterClass switch
        {
            eCharacterClass.Cleric => [Specs.Rejuvenation, Specs.Enhancement, Specs.Smite],
            eCharacterClass.Friar => [Specs.Rejuvenation, Specs.Enhancement, Specs.Staff],
            eCharacterClass.Healer => [Specs.Mending, Specs.Augmentation, Specs.Pacification],
            eCharacterClass.Shaman => [Specs.Mending, Specs.Augmentation, Specs.Subterranean],
            eCharacterClass.Druid => [Specs.Regrowth, Specs.Nurture, Specs.Nature],
            eCharacterClass.Warden => [Specs.Regrowth, Specs.Nurture],
            eCharacterClass.Mentalist => [Specs.Light, Specs.Mana, Specs.Mentalism],
            eCharacterClass.Eldritch => [Specs.Light, Specs.Mana, Specs.Void],
            eCharacterClass.Enchanter => [Specs.Light, Specs.Mana, Specs.Enchantments],
            eCharacterClass.Sorcerer => [Specs.Matter_Magic, Specs.Body_Magic, Specs.Mind_Magic],
            eCharacterClass.Theurgist => [Specs.Earth_Magic, Specs.Cold_Magic, Specs.Wind_Magic],
            eCharacterClass.Bard => [Specs.Regrowth, Specs.Nurture, Specs.Music],
            _ => [],
        };

        private static string LineFromLabel(eSpecType label) => label switch
        {
            eSpecType.RejuvCleric or eSpecType.RejuvFriar => Specs.Rejuvenation,
            eSpecType.EnhanceCleric or eSpecType.EnhanceFriar => Specs.Enhancement,
            eSpecType.SmiteCleric => Specs.Smite,
            eSpecType.StaffFriar => Specs.Staff,
            eSpecType.MendHealer or eSpecType.MendShaman => Specs.Mending,
            eSpecType.AugHealer or eSpecType.AugShaman => Specs.Augmentation,
            eSpecType.PacHealer => Specs.Pacification,
            eSpecType.SubtShaman => Specs.Subterranean,
            eSpecType.RegrowthDruid or eSpecType.RegrowthWarden or eSpecType.RegrowthBard => Specs.Regrowth,
            eSpecType.NurtureDruid or eSpecType.NurtureWarden or eSpecType.NurtureBard => Specs.Nurture,
            eSpecType.NatureDruid => Specs.Nature,
            eSpecType.MusicBard => Specs.Music,
            eSpecType.LightMenta or eSpecType.LightEld or eSpecType.LightEnchanter => Specs.Light,
            eSpecType.ManaMenta or eSpecType.ManaEld or eSpecType.ManaEnchanter => Specs.Mana,
            eSpecType.MentaMenta => Specs.Mentalism,
            eSpecType.VoidEld => Specs.Void,
            eSpecType.EnchantmentEnchanter => Specs.Enchantments,
            eSpecType.MatterSorc => Specs.Matter_Magic,
            eSpecType.BodySorc => Specs.Body_Magic,
            eSpecType.MindSorc => Specs.Mind_Magic,
            eSpecType.EarthTheur => Specs.Earth_Magic,
            eSpecType.IceTheur => Specs.Cold_Magic,
            eSpecType.AirTheur => Specs.Wind_Magic,
            _ => null,
        };

        /// <summary>
        /// Pure: how well a class and spec fills a group's healer slot (lower is better). Rejuvenation Cleric, Mending or
        /// Augmentation Healer, Regrowth Druid and Mending Shaman first; then Enhancement Cleric, Pacification Healer,
        /// Nurture Druid, Augmentation Shaman, Rejuvenation Friar and Regrowth Warden; a Mentalism Mentalist, Nature Druid,
        /// Subterranean Shaman or other Warden backs up; a Smite Cleric heals only in a pinch; a Paladin or a Friar with
        /// Rejuvenation under half its level is the last resort.
        /// </summary>
        public static int HealerRank(eCharacterClass characterClass, string mainLine, int rejuvenation = 0, int level = 1) =>
            characterClass switch
            {
                eCharacterClass.Cleric => mainLine == Specs.Smite ? PinchHealer : mainLine == Specs.Enhancement ? StrongHealer : PrimaryHealer,
                eCharacterClass.Healer => mainLine == Specs.Pacification ? StrongHealer : PrimaryHealer,
                eCharacterClass.Druid => mainLine == Specs.Nature ? BackupHealer : mainLine == Specs.Nurture ? StrongHealer : PrimaryHealer,
                eCharacterClass.Shaman => mainLine == Specs.Subterranean ? BackupHealer : mainLine == Specs.Augmentation ? StrongHealer : PrimaryHealer,
                eCharacterClass.Friar => rejuvenation * 2 >= Math.Max(1, level) ? StrongHealer : LastResortHealer,
                eCharacterClass.Warden => mainLine == Specs.Regrowth ? StrongHealer : BackupHealer,
                eCharacterClass.Mentalist => mainLine == Specs.Mentalism ? BackupHealer : NoHealer,
                eCharacterClass.Paladin => LastResortHealer,
                eCharacterClass.Acolyte or eCharacterClass.Seer or eCharacterClass.Naturalist => StrongHealer,
                _ => NoHealer,
            };

        /// <summary>A real healer for a group's healer slot (rank 0 or 1).</summary>
        public static bool IsPrimaryHealer(int rank) => rank <= StrongHealer;

        /// <summary>
        /// Pure: how well a class and spec fills the crowd-control slot (lower is better): Enchanter, Mind Sorcerer,
        /// Pacification Healer and Music Bard first; then other Bards, Minstrel, Light Eldritch and Light or Mentalism
        /// Mentalist; then Air Theurgist, Skald and the other Sorcerers and Healers.
        /// </summary>
        public static int CrowdControlRank(eCharacterClass characterClass, string mainLine) => characterClass switch
        {
            eCharacterClass.Enchanter => 0,
            eCharacterClass.Sorcerer => mainLine == Specs.Mind_Magic ? 0 : 2,
            eCharacterClass.Healer => mainLine == Specs.Pacification ? 0 : 2,
            eCharacterClass.Bard => mainLine == Specs.Music ? 0 : 1,
            eCharacterClass.Minstrel => 1,
            eCharacterClass.Eldritch => mainLine == Specs.Light ? 1 : NoCrowdControl,
            eCharacterClass.Mentalist => mainLine is Specs.Light or Specs.Mentalism ? 1 : NoCrowdControl,
            eCharacterClass.Theurgist => mainLine == Specs.Wind_Magic ? 2 : NoCrowdControl,
            eCharacterClass.Skald => 2,
            _ => NoCrowdControl,
        };

        /// <summary>Pure: a tank that trained Shields to at least half its level is a shield tank (main tank and puller).</summary>
        public static bool IsShieldTank(bool tankClass, int shieldSpec, int level) =>
            tankClass && BotWeaponSetPolicy.ShieldTrainedForGuard(shieldSpec, level);

        public static eCharacterClass ClassOf(GameBot bot) => (eCharacterClass)(bot?.CharacterClass?.ID ?? 0);

        public static string MainLine(GameBot bot) =>
            bot?.CharacterClass == null ? null : MainLine(ClassOf(bot), bot.GetBaseSpecLevel, bot.BotSpec?.SpecType ?? eSpecType.None);

        public static int HealerRank(GameBot bot) => bot?.CharacterClass == null ? NoHealer :
            HealerRank(ClassOf(bot), MainLine(bot), bot.GetBaseSpecLevel(Specs.Rejuvenation), bot.Level);

        public static int CrowdControlRank(GameBot bot) => bot?.CharacterClass == null ? NoCrowdControl :
            CrowdControlRank(ClassOf(bot), MainLine(bot));

        public static bool IsShieldTank(GameBot bot) => bot?.CharacterClass != null &&
            IsShieldTank(BotPartyRoles.IsTank(bot), bot.GetBaseSpecLevel(Specs.Shields), bot.Level);

        /// <summary>
        /// Pure: the order tanks take the main-tank job: the party's puller when it is a tank, then shield tanks, then the
        /// higher level, then the given order. Returns tank indexes, main tank first.
        /// </summary>
        public static int[] RankTanks(IReadOnlyList<bool> shield, IReadOnlyList<int> levels, int pullerIndex)
        {
            int count = shield?.Count ?? 0;
            int[] order = new int[count];
            for (int index = 0; index < count; index++)
                order[index] = index;
            Array.Sort(order, (a, b) =>
            {
                int compare = (b == pullerIndex).CompareTo(a == pullerIndex);
                if (compare == 0) compare = shield[b].CompareTo(shield[a]);
                if (compare == 0) compare = levels[b].CompareTo(levels[a]);
                return compare != 0 ? compare : a.CompareTo(b);
            });
            return order;
        }

        /// <summary>
        /// Pure: swaps that put better healers in the template's Healer slots. A member in another slot moves into a Healer
        /// slot when it heals better than the Healer slot's member and that member can take its slot. A backup healer (a
        /// Mentalism Mentalist) may hold a Healer slot only beside a real healer. Returns the slot pairs swapped, in order.
        /// </summary>
        public static List<(int, int)> PreferBetterHealers(BotPveGroupRole[] template, eCharacterClass[] slotClasses,
            int[] slotRanks)
        {
            var swaps = new List<(int, int)>();
            if (template == null || slotClasses == null || slotRanks == null)
                return swaps;
            for (int healerSlot = 0; healerSlot < template.Length; healerSlot++)
            {
                if (template[healerSlot] != BotPveGroupRole.Healer)
                    continue;
                int bestSlot = -1;
                for (int slot = 0; slot < template.Length; slot++)
                {
                    if (template[slot] == BotPveGroupRole.Healer || slotRanks[slot] >= slotRanks[healerSlot] ||
                        !BotPartyRoles.CanFill(slotClasses[healerSlot], template[slot]) ||
                        !CanHoldHealerSlot(template, slotClasses, slotRanks, healerSlot, slot))
                        continue;
                    if (bestSlot < 0 || slotRanks[slot] < slotRanks[bestSlot])
                        bestSlot = slot;
                }
                if (bestSlot < 0)
                    continue;
                (slotClasses[healerSlot], slotClasses[bestSlot]) = (slotClasses[bestSlot], slotClasses[healerSlot]);
                (slotRanks[healerSlot], slotRanks[bestSlot]) = (slotRanks[bestSlot], slotRanks[healerSlot]);
                swaps.Add((healerSlot, bestSlot));
            }
            return swaps;
        }

        private static bool CanHoldHealerSlot(BotPveGroupRole[] template, eCharacterClass[] slotClasses, int[] slotRanks,
            int healerSlot, int candidateSlot)
        {
            if (BotPartyRoles.CanFill(slotClasses[candidateSlot], BotPveGroupRole.Healer))
                return true;
            if (slotRanks[candidateSlot] != BackupHealer)
                return false;
            for (int slot = 0; slot < template.Length; slot++)
                if (slot != healerSlot && template[slot] == BotPveGroupRole.Healer && IsPrimaryHealer(slotRanks[slot]))
                    return true;
            return false;
        }

        /// <summary>Pure: the crowd-control slot's member index (lowest rank, then the given order), or -1.</summary>
        public static int ChooseCrowdControl(IReadOnlyList<int> ranks, IReadOnlyList<bool> canControl, int partySize)
        {
            if (partySize < CrowdControlSlotMinimumSize || ranks == null)
                return -1;
            int best = -1;
            for (int index = 0; index < ranks.Count; index++)
                if (ranks[index] < NoCrowdControl && (canControl == null || canControl[index]) &&
                    (best < 0 || ranks[index] < ranks[best]))
                    best = index;
            return best;
        }

        /// <summary>
        /// Pure: the locked puller's order (lower pulls first) among members that can pull from range: a shield tank, a
        /// two-hander tank, the crowd-control slot (Freddyshouse: the CC pulls when no tank has a ranged pull), other
        /// members, healers last.
        /// </summary>
        public static int RangedPullerOrder(BotPveGroupRole? role, bool shieldTank, bool crowdControl) => role switch
        {
            BotPveGroupRole.Tank => shieldTank ? 0 : 1,
            _ when crowdControl => 2,
            BotPveGroupRole.Healer => 4,
            null => 5,
            _ => 3,
        };

        // ---------------------------------------------------------------- the role sheet

        /// <summary>A party's roles, worked out when it forms (and again when its puller or members change).</summary>
        public sealed class Sheet
        {
            public GameLiving[] Tanks = [];        // main tank first
            public bool[] ShieldTanks = [];        // parallel to Tanks
            public GameLiving[] Healers = [];      // best healer first
            public GameLiving CrowdControl;
            public long Signature;
        }

        private static readonly ConditionalWeakTable<Group, Sheet> Sheets = new();

        public static void Publish(Group group, Sheet sheet)
        {
            if (group == null)
                return;
            if (sheet == null)
                Sheets.Remove(group);
            else
                Sheets.AddOrUpdate(group, sheet);
        }

        public static Sheet Get(Group group) => group != null && Sheets.TryGetValue(group, out Sheet sheet) ? sheet : null;

        private static bool Present(GameLiving member, GameLiving near) =>
            member is { IsAlive: true, ObjectState: GameObject.eObjectState.Active } &&
            (near == null || member.CurrentRegionID == near.CurrentRegionID);

        /// <summary>The party's main tank as seen by <paramref name="member"/>: the first ranked tank alive in its region.</summary>
        public static GameLiving MainTank(GameLiving member)
        {
            Sheet sheet = Get(member?.Group);
            if (sheet == null)
                return null;
            foreach (GameLiving tank in sheet.Tanks)
                if (Present(tank, member) && member.Group.IsInTheGroup(tank))
                    return tank;
            return null;
        }

        public static bool IsMainTank(GameLiving member) => member != null && MainTank(member) == member;

        /// <summary>A ranked tank of a party with another living main tank.</summary>
        public static bool IsOffTank(GameLiving member)
        {
            Sheet sheet = Get(member?.Group);
            if (sheet == null || Array.IndexOf(sheet.Tanks, member) < 0)
                return false;
            GameLiving main = MainTank(member);
            return main != null && main != member;
        }

        /// <summary>A two-hander off-tank: damage while the main tank holds the kill (no taunting it, flanks it).</summary>
        public static bool IsDamageTank(GameLiving member)
        {
            Sheet sheet = Get(member?.Group);
            if (sheet == null)
                return false;
            int index = Array.IndexOf(sheet.Tanks, member);
            return index >= 0 && !sheet.ShieldTanks[index] && IsOffTank(member);
        }

        /// <summary>A living off-tank near <paramref name="mainTank"/> (within <paramref name="radius"/>), or null.</summary>
        public static GameLiving OffTankNear(GameLiving mainTank, int radius)
        {
            Sheet sheet = Get(mainTank?.Group);
            if (sheet == null)
                return null;
            foreach (GameLiving tank in sheet.Tanks)
                if (tank != mainTank && Present(tank, mainTank) && mainTank.Group.IsInTheGroup(tank) &&
                    mainTank.IsWithinRadius(tank, radius))
                    return tank;
            return null;
        }

        /// <summary>The party's crowd-control slot member, when alive.</summary>
        public static GameLiving CrowdControl(GameLiving member)
        {
            GameLiving cc = Get(member?.Group)?.CrowdControl;
            return Present(cc, member) && member.Group.IsInTheGroup(cc) ? cc : null;
        }
    }
}
