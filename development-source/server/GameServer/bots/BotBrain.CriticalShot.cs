using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>Q15 (veteran review 2026-11-11): an archer opens with Critical Shot (NpcAttackAction sets it on the draw at
    /// an idle monster); a Hunter's pet waits at heel for that opener instead of putting the monster in combat first.</summary>
    public partial class BotBrain
    {
        private GameLiving _criticalShotHoldTarget;
        private long _criticalShotHoldSince;

        /// <summary>Q15: whether the pet waits for this bot's Critical Shot opener on <paramref name="target"/>.</summary>
        public bool HoldsPetForCriticalShot(GameLiving target, IControlledBrain petBrain)
        {
            GameBot bot = BotBody;
            if (bot == null || target == null || !target.IsAlive || !CompanionControls165.CriticalShotAllowed(bot))
                return false;
            long now = GameLoop.GameLoopTime;
            if (_criticalShotHoldTarget != target)
            {
                _criticalShotHoldTarget = target;
                _criticalShotHoldSince = now;
            }
            // cheapest facts first: most calls end at "no bow in hand" or "already fighting"
            bool bowReady = bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance &&
                bot.Endurance >= RangeAttackComponent.CRITICAL_SHOT_ENDURANCE_COST + RangeAttackComponent.DEFAULT_ENDURANCE_COST;
            bool targetIdle = !target.InCombat && target.TargetObject != bot;
            if (!bowReady || !targetIdle)
                return false;
            bool petOnIt = petBrain is ControlledMobBrain controlled && controlled.OrderedAttackTarget == target ||
                           petBrain?.Body?.TargetObject == target && petBrain.Body.IsAttacking;
            return BotRangedCombat.HoldPetForCriticalShot(bot.GetAbilityLevel(Abilities.Critical_Shot) > 0,
                bowReady, targetIdle, petOnIt, now - _criticalShotHoldSince);
        }
    }
}
