using System.Linq;
using System.Numerics;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>O3 (owner 2026-11-11): a solo Shaman or Runemaster roots its monster and takes one step back
    /// (<see cref="BotSoloRootKitePolicy"/>). Gamebots and companions share it; a companion is always grouped, so it
    /// never kites.</summary>
    public partial class BotBrain
    {
        private long _rootKiteNextTick;
        private GameLiving _rootKiteTarget;
        private long _rootKiteLandBy;
        private int _rootKiteStep;
        private long _rootKiteStepUntil;

        /// <summary>Rooted (breakable or not) right now.</summary>
        private static bool IsRootedNow(GameLiving living) =>
            EffectListService.GetEffectOnTarget(living, eEffect.MovementSpeedDebuff) is ECSGameSpellEffect root &&
            root.SpellHandler?.Spell is { Value: >= 99 };

        /// <summary>True while the kite owns this turn (casting the root is the native cast's turn, then the step).</summary>
        private bool TrySoloRootKite(GameLiving target)
        {
            GameBot bot = BotBody;
            if (bot?.CharacterClass == null)
                return false;
            long now = GameLoop.GameLoopTime;

            // The step back: hold the turn while walking it, then the normal fight goes on.
            if (_rootKiteStepUntil != 0)
            {
                if (now < _rootKiteStepUntil && bot.IsMoving && target == _rootKiteTarget && target.IsAlive)
                    return true;
                _rootKiteStepUntil = 0;
                _rootKiteTarget = null;
                return false;
            }

            // The root was cast: once it shows on the monster, ONE straight step back (no route request).
            if (_rootKiteTarget != null)
            {
                if (target != _rootKiteTarget || !target.IsAlive || now > _rootKiteLandBy || bot.Group != null)
                {
                    _rootKiteTarget = null;
                    return false;
                }
                if (!IsRootedNow(target))
                    return bot.castingComponent?.HasPendingSkillRequests == true;
                int distance = (int)bot.GetDistanceTo(target);
                if (!BotSoloRootKitePolicy.TryStepPoint(bot.X, bot.Y, target.X, target.Y,
                        System.Math.Min(_rootKiteStep, BotSoloRootKitePolicy.MaxStepDistance), out int x, out int y))
                {
                    _rootKiteTarget = null;
                    return false;
                }
                bot.StopAttack();
                bot.StopMovingOnPath();
                bot.WalkTo(new Point3D(x, y, bot.Z), bot.MaxSpeed);
                _rootKiteStepUntil = now + BotSoloRootKitePolicy.StepHoldMilliseconds;
                if (log.IsInfoEnabled)
                    log.Info($"BOT_ROOT_KITE bot=\"{bot.Name}\" class=\"{bot.ClassName}\" level={bot.Level} " +
                             $"target=\"{target.Name}\" hp={target.HealthPercent}% from={distance} step={_rootKiteStep}");
                return true;
            }

            if (!BotSoloRootKitePolicy.ClassKites((eCharacterClass)bot.CharacterClass.ID) || now < _rootKiteNextTick ||
                bot.Group != null || bot.CrowdControlSpells == null || bot.IsCasting || bot.IsIncapacitated ||
                bot.castingComponent?.HasPendingSkillRequests == true)
                return false;
            double targetDistance = bot.GetDistanceTo(target);
            bool monster = target is GameNPC and not GameBot && target.Realm == eRealm.None &&
                !BotPvpCrowdControl.PlayerLike(target);
            bool held = target.IsMezzed || target.IsStunned || IsRootedNow(target);
            bool immune = target.effectListComponent?.ContainsEffectForEffectType(eEffect.SnareImmunity) == true;
            if (!BotSoloRootKitePolicy.ShouldRoot(true, bot.Group == null, monster, target.TargetObject == bot,
                    targetDistance, target.HealthPercent, held, immune, bot.CurrentZone?.IsDungeon == true, now,
                    _rootKiteNextTick))
                return false;

            Spell root = bot.CrowdControlSpells
                .Where(spell => ShamanBotCombatPolicy.IsRoot(spell) && spell.Target == eSpellTarget.ENEMY &&
                    spell.Radius <= 0 && spell.Level <= bot.Level && bot.Mana >= bot.PowerCost(spell) &&
                    bot.GetSkillDisabledDuration(spell) <= 0 && !(spell.CastTime > 0 && bot.IsBeingInterrupted) &&
                    bot.IsWithinRadius(target, spell.CalculateEffectiveRange(bot)))
                .OrderByDescending(spell => spell.Level).FirstOrDefault();
            if (root == null)
                return false;
            int step = BotSoloRootKitePolicy.StepDistance(targetDistance, root.CalculateEffectiveRange(bot));
            if (!BotSoloRootKitePolicy.TryStepPoint(bot.X, bot.Y, target.X, target.Y, step, out int stepX, out int stepY))
                return false;
            // One line-of-sight check for the straight step (a wall or a cliff edge behind the bot: no kite this time).
            _rootKiteNextTick = now + BotSoloRootKitePolicy.ReuseMilliseconds;
            if (bot.CurrentZone != null && !PathfindingProvider.Instance.HasLineOfSight(bot.CurrentZone,
                    new Vector3(bot.X, bot.Y, bot.Z + 30), new Vector3(stepX, stepY, bot.Z + 30),
                    PathfindingProvider.Instance.DefaultFilters))
                return false;

            bot.StopAttack();
            bot.StopMovingOnPath();
            bot.StopMoving();
            if (!CheckOffensiveSpells(root))
                return false;
            _rootKiteTarget = target;
            _rootKiteStep = step;
            _rootKiteLandBy = now + root.CastTime + BotSoloRootKitePolicy.RootLandGraceMilliseconds;
            return true;
        }
    }
}
