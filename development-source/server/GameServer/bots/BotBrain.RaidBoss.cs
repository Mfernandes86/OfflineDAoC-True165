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

        private static long _nextLairLog;
        private long _lairMoveAt;
        private const long LairMoveReissueMilliseconds = 1_500;

        /// <summary>
        /// dragon-fight L1: a dragon takes no damage more than 2,000 from its lair, but the bots reacted only to its 2,500
        /// tether (AutonomousTetherReset), so between the two the raid hit an immune dragon and its posts followed it out.
        /// A boss with a lair circle (<see cref="IRaidBossTraits.RaidLairRadius"/>) that is outside it is treated as the
        /// tether rule treats a boss outside its tether, at the lair's radius: the bot stops hitting it; the one it chases
        /// walks home so it follows (as veterans led a dragon back); a bot standing outside the circle walks back inside;
        /// the rest wait. No leash here: beyond the tether the tether rule still does that. True when the turn is used.
        /// Cost: one distance check per think of a bot whose target has a lair circle; a walk order at most every 1.5 s.
        /// </summary>
        private bool TryHoldForBossOutsideLair(GameNPC boss)
        {
            GameBot bot = BotBody;
            if (bot?.IsAutonomousWorldBot != true || bot.IsPlayerLedGroup || !boss.IsAlive)
                return false;
            IRaidBossTraits traits = RaidTraitsOf(boss);
            if (traits == null || traits.RaidLairRadius <= 0 || traits.RaidLairCentre is not Point3D centre ||
                boss.IsWithinRadius(centre, traits.RaidLairRadius))
                return false;
            if (bot.IsCasting && bot.castingComponent?.SpellHandler?.Spell?.IsHarmful == true)
                bot.StopCurrentSpellcast();
            bot.StopAttack();
            if (bot.ControlledBrain?.Body?.TargetObject == boss)
                bot.ControlledBrain.Disengage();
            int radius = traits.RaidLairRadius;
            RaidBossTraits.LairAction action = RaidBossTraits.OutsideLairAction(boss.TargetObject == bot,
                !bot.IsWithinRadius(centre, radius));
            long now = GameLoop.GameLoopTime;
            if (action == RaidBossTraits.LairAction.Hold)
                bot.StopFollowing();
            else if (now - _lairMoveAt >= LairMoveReissueMilliseconds || !bot.IsMoving)
            {
                _lairMoveAt = now;
                bot.StopFollowing();
                if (action == RaidBossTraits.LairAction.Lead)
                    bot.PathTo(new Point3D(centre.X, centre.Y, centre.Z), bot.MaxSpeed);
                else
                {
                    (int x, int y) = RaidBossTraits.PointInsideCircle(centre.X, centre.Y, bot.X, bot.Y, radius,
                        RaidBossTraits.LairReturnMargin);
                    bot.PathTo(new Point3D(x, y, centre.Z), bot.MaxSpeed);
                }
            }
            if (log.IsInfoEnabled && now >= _nextLairLog)
            {
                _nextLairLog = now + 10_000;
                log.Info($"REALM_BOSS_OUTSIDE_LAIR boss=\"{boss.Name}\" region={boss.CurrentRegionID} bot={bot.Name} " +
                         $"action={action} fromLair={(int)boss.GetDistanceTo(centre)} lair={radius}");
            }
            return true;
        }
        public const long BladeturnCheckMilliseconds = 1_000;
        private long _bladeturnCheckAt;
        private bool _bladeturnFeedsBoss;

        private static bool ConsumesBladeturn(GameObject target) =>
            target is IRaidBossTraits { RaidConsumesBladeturn: true } && target is GameLiving { IsAlive: true };

        /// <summary>
        /// df-bosses L3: Legion strips bladeturn from his target every turn and after five casts a 1,000 Body blast on
        /// everyone within 1,000 that heals him a quarter of each hit. Bots recast self bladeturns, cast any missing
        /// bladeturn and Wardens held the pulsing chant in a fight, so they fed it. True while the bot's own target, its
        /// party's kill target or a target of its party's fight board consumes bladeturn; then it casts, pulses and keeps
        /// no bladeturn (a Warden holds its damage add), and a running pulsing bladeturn of its own ends at once.
        /// Cost: rechecked at most once a second (type tests on the bot's target, the kill target and the party's
        /// targets on the existing board, which is never rebuilt here); no scan, no allocation.
        /// </summary>
        private bool BladeturnFeedsBoss()
        {
            long now = GameLoop.GameLoopTime;
            if (now < _bladeturnCheckAt)
                return _bladeturnFeedsBoss;
            _bladeturnCheckAt = now + BladeturnCheckMilliseconds;
            bool own = ConsumesBladeturn(Body.TargetObject);
            bool kill = !own && Body.Group is { } group && KillTargets.TryGetValue(group, out KillTargetHolder holder) &&
                ConsumesBladeturn(holder.Target);
            bool party = false;
            if (!own && !kill && Body.Group != null && PeekFightBoard(Body) is { } board)
                foreach (GameLiving target in board.SelectedTargets)
                    if (ConsumesBladeturn(target))
                    {
                        party = true;
                        break;
                    }
            bool feeds = RaidBossTraits.BladeturnFeedsBoss(own, kill, party);
            if (feeds && !_bladeturnFeedsBoss)
                EndOwnPulsingBladeturn();
            _bladeturnFeedsBoss = feeds;
            return feeds;
        }

        /// <summary>A bladeturn spell this bot must not cast now (<see cref="BladeturnFeedsBoss"/>).</summary>
        private bool BladeturnBlocked(Spell spell) => spell?.SpellType == eSpellType.Bladeturn && BladeturnFeedsBoss();

        private void EndOwnPulsingBladeturn()
        {
            if (Body?.effectListComponent?.ContainsEffectForEffectType(eEffect.Pulse) != true)
                return;
            foreach (ECSPulseEffect pulse in Body.effectListComponent.GetPulseEffects())
                if (!pulse.IsEnding && !pulse.IsEnded && pulse.SpellHandler?.Spell?.SpellType == eSpellType.Bladeturn)
                    pulse.End();
        }
    }
}
