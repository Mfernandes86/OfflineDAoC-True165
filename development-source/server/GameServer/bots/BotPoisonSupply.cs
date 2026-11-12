using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>Unlimited supplies, not unlimited procs: a native coat per weapon outside combat.</summary>
    public static class BotPoisonSupply
    {
        private sealed record Coat(int Level, int SpellId, eSpellType Type);
        private static Coat[] _catalog = Array.Empty<Coat>();
        public static bool IsAssassin(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Infiltrator or eCharacterClass.Shadowblade or eCharacterClass.Nightshade;
        public static int EligibleLevel(int level, int envenom) => Math.Clamp(Math.Min(level, envenom), 0, 50);

        // One startup query; never reads SQLite during AI turns.
        public static void Load()
        {
            Load(GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Object_Type").IsEqualTo((int)eObjectType.Poison)),
                SkillBase.GetSpellList(GlobalSpellsLines.Mundane_Poisons));
        }

        public static void Load(IEnumerable<DbItemTemplate> items, IEnumerable<Spell> spellList)
        {
            var spells = spellList.GroupBy(s => s.ID).ToDictionary(g => g.Key, g => g.First());
            _catalog = items
                .Where(item => item.Level is > 0 and <= 50 && item.PoisonCharges > 0 &&
                    spells.TryGetValue(item.PoisonSpellID, out Spell spell) &&
                    spell.SpellType is eSpellType.DamageOverTime or eSpellType.StrengthConstitutionDebuff)
                .Select(item => new Coat(Math.Max(item.Level, spells[item.PoisonSpellID].Level),
                    item.PoisonSpellID, spells[item.PoisonSpellID].SpellType))
                .Distinct().OrderByDescending(coat => coat.Level).ToArray();
        }

        public static bool Maintain(GameBot bot)
        {
            if (bot?.CharacterClass == null || !IsAssassin((eCharacterClass)bot.CharacterClass.ID) ||
                !bot.IsAlive || bot.InCombat || bot.IsAttacking || bot.IsCasting || bot.IsCrowdControlled ||
                bot.castingComponent?.HasPendingSkillRequests == true) return false;
            int level = EligibleLevel(bot.Level, bot.GetModifiedSpecLevel(Specs.Envenom));
            // A hired companion uses the vials its player put in its bag, any kind, top to bottom (slot 1 first), on the
            // weapons it is holding, and only on a weapon with no charges left, so no coat is wasted (owner 2026-11-01).
            if (HiredCompanions165.IsHired(bot))
            {
                bool coated = false;
                foreach (eInventorySlot slot in WieldedWeaponSlots(bot))
                    coated |= Supplies165.CoatFromVial(bot, slot, level, _ => true, bagOrder: true);
                if (coated) Changed(bot);
                return coated;
            }
            // True 1.65 Mode: only real vials from the bag, used up (Supplies165); no free coats
            if (Classic165Mode.IsOn)
            {
                bool rvr = bot.CurrentRegion?.IsRvR == true;
                bool coated = CoatFirst(bot, eInventorySlot.RightHandWeapon, MainHandPoisons(rvr), level);
                coated |= CoatFirst(bot, eInventorySlot.TwoHandWeapon, MainHandPoisons(rvr), level);
                coated |= CoatFirst(bot, eInventorySlot.LeftHandWeapon, OffHandPoisons(rvr), level);
                if (coated) Changed(bot);
                return coated;
            }
            Coat damage = null, debuff = null;
            foreach (Coat coat in _catalog)
            {
                if (coat.Level > level) continue;
                if (damage == null && coat.Type == eSpellType.DamageOverTime) damage = coat;
                if (debuff == null && coat.Type == eSpellType.StrengthConstitutionDebuff) debuff = coat;
                if (damage != null && debuff != null) break;
            }
            bool changed = Apply(bot, eInventorySlot.RightHandWeapon, damage);
            changed |= Apply(bot, eInventorySlot.TwoHandWeapon, damage);
            changed |= Apply(bot, eInventorySlot.LeftHandWeapon, debuff ?? damage);
            if (changed) Changed(bot);
            return changed;
        }

        private static readonly eSpellType[] PveMainHand = [eSpellType.DamageOverTime];
        private static readonly eSpellType[] RvrMainHand = [eSpellType.DamageOverTime, eSpellType.Disease];
        private static readonly eSpellType[] PveOffHand = [eSpellType.StrengthConstitutionDebuff, eSpellType.DamageOverTime];
        private static readonly eSpellType[] RvrOffHand =
            [eSpellType.UnbreakableSpeedDecrease, eSpellType.StrengthConstitutionDebuff, eSpellType.DamageOverTime];

        /// <summary>
        /// Q32 (veteran review 2026-11-11; period assassins: crippling poison on the off-hand so the enemy cannot run,
        /// lethal or disease on the main hand): the main-hand vials in order of preference, PvE or RvR.
        /// </summary>
        public static IReadOnlyList<eSpellType> MainHandPoisons(bool rvr) => rvr ? RvrMainHand : PveMainHand;

        /// <summary>Q32: the off-hand vials in order of preference: crippling first in RvR, weakening in PvE.</summary>
        public static IReadOnlyList<eSpellType> OffHandPoisons(bool rvr) => rvr ? RvrOffHand : PveOffHand;

        /// <summary>Q32: the kinds an assassin gamebot keeps in its bag: crippling too while its job is RvR.</summary>
        public static IReadOnlyList<eSpellType> StockedPoisons(bool rvrJob) => rvrJob
            ? [eSpellType.DamageOverTime, eSpellType.StrengthConstitutionDebuff, eSpellType.UnbreakableSpeedDecrease]
            : [eSpellType.DamageOverTime, eSpellType.StrengthConstitutionDebuff];

        private static bool CoatFirst(GameBot bot, eInventorySlot slot, IReadOnlyList<eSpellType> order, int level)
        {
            foreach (eSpellType type in order)
                if (Supplies165.CoatFromVial(bot, slot, type, level))
                    return true;
            return false;
        }

        /// <summary>The weapons the bot fights with now: the two-hander, or the main hand and a left-hand weapon.</summary>
        private static IEnumerable<eInventorySlot> WieldedWeaponSlots(GameBot bot) => bot.ActiveWeaponSlot switch
        {
            eActiveWeaponSlot.TwoHanded => [eInventorySlot.TwoHandWeapon],
            eActiveWeaponSlot.Standard => [eInventorySlot.RightHandWeapon, eInventorySlot.LeftHandWeapon],
            _ => [],
        };

        private static bool Apply(GameBot bot, eInventorySlot slot, Coat coat)
        {
            DbInventoryItem weapon = bot.Inventory?.GetItem(slot);
            if (coat == null || !BotWeaponStats.FitsConfiguredSlot(bot, weapon, slot) || weapon.PoisonCharges > 0)
                return false;
            weapon.PoisonSpellID = coat.SpellId;
            weapon.PoisonMaxCharges = 1;
            weapon.PoisonCharges = 1;
            return true;
        }

        public static void Changed(GameBot bot)
        {
            if (!bot.IsAutonomousWorldBot) return;
            bot.MarkAutonomousStateDirty();
            // A coat changes neither equipment suitability nor backpack space;
            // don't invalidate the expensive vendor/upgrade appraisal cache.
            AutonomousBotStatusPersistence.Queue(bot, true);
        }
    }
}
