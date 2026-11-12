using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Q16 (veteran review 2026-11-11; Prima, Healer: the Pacification Healer's group role is control - the stun as the
    /// tank engages): once per fight a grouped Pacification Healer stuns the group's kill target when the tank has just
    /// engaged it, with half its power or more and no add needing control. True 1.65 Mode; numbers unchanged.
    /// </summary>
    public partial class BotBrain
    {
        private GameLiving _openingStunDoneFor;

        /// <summary>The opening stun goes only on a fresh kill target (at least this much health).</summary>
        public const int OpeningStunTargetHealthPercent = 75;

        /// <summary>Pure: the opening stun's conditions.</summary>
        public static bool OpeningStunWorthwhile(bool pacificationHealer, bool grouped, bool tankEngaged, bool alreadyDone,
            int healerPowerPercent, int targetHealthPercent, int monstersOnGroup, bool targetStunImmune) =>
            pacificationHealer && grouped && tankEngaged && !alreadyDone && healerPowerPercent >= 50 &&
            targetHealthPercent >= OpeningStunTargetHealthPercent && monstersOnGroup <= 1 && !targetStunImmune;

        /// <summary>A Healer built for Pacification: its Pac template, or Pacification trained above 1 and at least as
        /// high as Mending.</summary>
        private bool IsPacificationHealer => BotBody?.CharacterClass?.ID == (int)eCharacterClass.Healer &&
            (BotBody.BotSpec?.SpecType == eSpecType.PacHealer ||
             BotCasterPriority.SmiteSpecced(BotBody.GetBaseSpecLevel(Specs.Pacification), BotBody.GetBaseSpecLevel(Specs.Mending)));

        private bool TryHealerOpeningStun()
        {
            GameBot bot = BotBody;
            if (!Classic165Mode.IsOn || bot?.Group == null || bot.IsCasting || bot.IsBeingInterrupted ||
                bot.CharacterClass?.ID != (int)eCharacterClass.Healer ||
                // M7: the Crowd control row (Adds only and Off never control the kill target)
                !CompanionControls165.CcAllowsKillTargetControl(CompanionControls165.CrowdControl(bot)))
                return false;
            if (GroupKillTarget() is not GameNPC target || target is GameBot || !target.IsAlive ||
                target == _openingStunDoneFor || BotPvpCrowdControl.PlayerLike(target))
                return false;
            bool tankEngaged = target.TargetObject is GameLiving tank && tank.IsAlive && BotHealTriage165.IsTank(tank) &&
                               bot.Group.IsInTheGroup(tank) && AttacksTarget(tank, target);
            if (!tankEngaged)
                return false;
            bool stunImmune = target.effectListComponent.ContainsEffectForEffectType(eEffect.StunImmunity) ||
                              target.effectListComponent.ContainsEffectForEffectType(eEffect.NPCStunImmunity) ||
                              target.effectListComponent.ContainsEffectForEffectType(eEffect.Stun);
            if (!OpeningStunWorthwhile(IsPacificationHealer, true, true, false, bot.ManaPercent, target.HealthPercent,
                    MonstersOnGroupCached(), stunImmune))
                return false;
            Spell stun = (bot.HarmfulSpells ?? []).Concat(bot.InstantHarmfulSpells ?? [])
                .Where(spell => BotControlSpells.IsStun(spell) && !BotControlSpells.IsLongReuse(spell) && spell.Radius <= 0 &&
                                spell.Level <= bot.Level && bot.Mana >= bot.PowerCost(spell) &&
                                (!spell.HasRecastDelay || bot.GetSkillDisabledDuration(spell) <= 0) &&
                                bot.IsWithinRadius(target, spell.CalculateEffectiveRange(bot)))
                .OrderByDescending(spell => spell.Level)
                .FirstOrDefault();
            if (stun == null || !BotGroupSupport.HasCorpseLineOfSight(bot, target))
                return false;
            _openingStunDoneFor = target;   // once per fight, whether or not it lands
            GameObject previous = bot.TargetObject;
            bot.TargetObject = target;
            try
            {
                return CheckOffensiveSpells(stun);
            }
            finally
            {
                bot.TargetObject = previous;
            }
        }
    }
}
