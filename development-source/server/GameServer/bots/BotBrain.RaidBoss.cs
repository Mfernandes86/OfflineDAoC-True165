using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// What a gamebot does about a raid boss's own rules (realm-event fixes 2026-11-12, <see cref="IRaidBossTraits"/>).
    /// The boss's traits are read once when the bot's target changes and kept with the target reference, so every
    /// check after that is a reference compare and a field read: no scan, no allocation.
    /// </summary>
    public partial class BotBrain
    {
        private GameObject _raidTraitsFor;
        private IRaidBossTraits _raidTraits;

        /// <summary>The traits of <paramref name="target"/>, resolved only when the target changes (null when none).</summary>
        private IRaidBossTraits RaidTraitsOf(GameObject target)
        {
            if (!ReferenceEquals(target, _raidTraitsFor))
            {
                _raidTraitsFor = target;
                _raidTraits = target as IRaidBossTraits;
            }
            return _raidTraits;
        }

        /// <summary>
        /// df-bosses L2: a ranged bot's stand-off range against <paramref name="target"/>. Legion takes no damage from
        /// beyond 1,400, but casters walked to their longest spell range less 100 (bolts: 1,775) and archers to bow range
        /// less 100 (1,660 and more), so all their damage was void. Capped at the boss's limit less a margin.
        /// </summary>
        private int CapRangeForTarget(GameObject target, int ownRange)
        {
            IRaidBossTraits traits = RaidTraitsOf(target);
            return traits == null ? ownRange : RaidBossTraits.CapStandOff(ownRange, traits.RaidMaxDamageRange);
        }

        /// <summary>The bot stands where its current target takes no damage from it: no attack spell from here, walk in.</summary>
        private bool BeyondTargetDamageRange()
        {
            GameObject target = Body.TargetObject;
            IRaidBossTraits traits = RaidTraitsOf(target);
            return traits != null && traits.RaidMaxDamageRange > 0 &&
                RaidBossTraits.BeyondDamageRange(traits.RaidMaxDamageRange, Body.GetDistanceTo(target));
        }
    }
}
