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
        private long _areaUntil;
        private int _areaX, _areaY, _areaRadius;
        private bool _areaOnBoss;
        private long _areaMoveAt;
        /// <summary>How long a seen area spell is still avoided after the last think that saw it being cast.</summary>
        public const long AreaHoldMilliseconds = 1_500;

        /// <summary>
        /// epic-bosses L2, sh-bosses: bots stood in a boss's area spells - Apocalypse's Rain of Fire (650 every 3 s within
        /// 800 for 30 s, twice), Govannon's damage over time round his target, Bane of Hope's. The server has no "ground
        /// effect" object: these are spells the boss itself is casting, readable from its casting component (the spell's
        /// radius and target type, the boss's ground target or the cast target). On its existing think a bot whose
        /// target is an epic encounter casting a harmful spell with a radius and a cast time steps out when it can keep
        /// fighting from outside (<see cref="RaidBossTraits.StepsOutOfArea"/>), and a melee bot waits outside while the
        /// cast lasts and the boss stands in it. Instant area spells (Xaga's lights, the fire primal's patches, the Rain
        /// of Fire add's bolt) give no warning and are not dodged. True when the turn is used.
        /// Cost: a type test and a cast read per think; a distance only while the target casts such a spell; a walk
        /// order at most every second. No scan, no allocation.
        /// </summary>
        private bool TryStepOutOfBossArea(GameLiving target)
        {
            GameBot bot = BotBody;
            if (bot == null || target is not GameNPC boss || boss is not IGameEpicNpc || !boss.IsAlive)
                return false;
            long now = GameLoop.GameLoopTime;
            bool castTargetMe = false;
            if (boss.IsCasting && boss.castingComponent?.SpellHandler is { } handler &&
                handler.Spell is { IsHarmful: true, Radius: > 0, CastTime: > 0 } spell)
            {
                RaidBossTraits.AreaCentre kind = RaidBossTraits.CentreOf(spell.Target, spell.Range, spell.Radius);
                GameObject centre = null;
                int x = 0, y = 0;
                switch (kind)
                {
                    case RaidBossTraits.AreaCentre.Ground when boss.GroundTarget.IsValid:
                        x = boss.GroundTarget.X; y = boss.GroundTarget.Y;
                        break;
                    case RaidBossTraits.AreaCentre.CastTarget when handler.Target is { } castTarget:
                        centre = castTarget;
                        castTargetMe = castTarget == bot;
                        break;
                    case RaidBossTraits.AreaCentre.Caster:
                        centre = boss;
                        break;
                    default:
                        kind = RaidBossTraits.AreaCentre.None;
                        break;
                }
                if (kind != RaidBossTraits.AreaCentre.None)
                {
                    if (centre != null)
                    {
                        x = centre.X; y = centre.Y;
                    }
                    _areaX = x; _areaY = y; _areaRadius = spell.Radius;
                    _areaOnBoss = kind == RaidBossTraits.AreaCentre.Caster ||
                        boss.IsWithinRadius(new Point3D(x, y, boss.Z), spell.Radius) && kind == RaidBossTraits.AreaCentre.Ground;
                    _areaUntil = now + AreaHoldMilliseconds;
                }
            }
            if (now >= _areaUntil)
                return false;

            long reach = _areaRadius + RaidBossTraits.AreaStepMargin;
            long bx = bot.X - _areaX, by = bot.Y - _areaY;
            bool inside = bx * bx + by * by < reach * reach;
            bool ranged = PrefersCurrentSpellRange() || bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance ||
                bot.CanCastHealSpells && !IsTankClass;
            bool holds = boss.TargetObject == bot;
            if (RaidBossTraits.StepsOutOfArea(inside, castTargetMe, ranged, holds, bot.HealthPercent, _areaOnBoss,
                    bot.IsWithinRadius(boss, bot.MeleeAttackRange + 50)))
            {
                if (now - _areaMoveAt >= 1_000 || !bot.IsMoving)
                {
                    _areaMoveAt = now;
                    if (bot.IsAttacking && !holds)
                        bot.StopAttack();
                    bot.StopFollowing();
                    (int x, int y) = RaidBossTraits.PointOutsideCircle(_areaX, _areaY, bot.X, bot.Y, _areaRadius,
                        RaidBossTraits.AreaStepMargin, boss.X, boss.Y);
                    bot.PathTo(new Point3D(x, y, bot.Z), bot.MaxSpeed);
                }
                return true;
            }
            long ox = boss.X - _areaX, oy = boss.Y - _areaY;
            if (RaidBossTraits.WaitsOutsideArea(true, inside, ranged, holds, ox * ox + oy * oy < reach * reach))
            {
                if (bot.IsAttacking)
                    bot.StopAttack();
                bot.StopFollowing();
                return true;
            }
            return false;
        }
        /// <summary>
        /// sh-bosses M2, df-bosses M5: a boss a summoned pet cannot hurt. Summoner Lossren takes 5 damage from any summoned
        /// pet (his own TakeDamage), so the pet classes' pets did nothing on him while his souls ran free. (The bosses lane
        /// may later state this as a raid-boss trait; it is the one such boss in the reports.)
        /// </summary>
        public static bool PetsDoNoDamageTo(GameLiving target) => target is SummonerLossren;

        private long _petCallOffAt;

        /// <summary>
        /// The pet's target when the owner's is a boss pets cannot hurt: an add on the group from the fight board (the
        /// board is only peeked at, never rebuilt here), one on the owner or the pet first; null when there is none, and
        /// then a pet already on the boss is called back to its owner (at most once a second).
        /// Cost: a walk over the board's few NPCs, only while the owner's target is such a boss.
        /// </summary>
        private GameLiving PetTargetInsteadOfBoss(GameLiving boss, ControlledMobBrain petBrain)
        {
            GameLiving best = null;
            if (PeekFightBoard(Body) is { } board)
                foreach (GameNPC npc in board.NpcsOnGroup)
                {
                    if (npc == boss || PetsDoNoDamageTo(npc) || !IsValidPetCombatTarget(petBrain.Body, npc))
                        continue;
                    if (npc.TargetObject == Body || npc.TargetObject == petBrain.Body)
                    {
                        best = npc;
                        break;
                    }
                    best ??= npc;
                }
            bool callOff = best == null;
            long now = GameLoop.GameLoopTime;
            if (callOff && petBrain.Body?.TargetObject == boss && now >= _petCallOffAt)
            {
                _petCallOffAt = now + 1_000;
                petBrain.Disengage();
                petBrain.Follow(Body);
            }
            return best;
        }
    }
}
