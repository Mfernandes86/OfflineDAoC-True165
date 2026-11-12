using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.GS.Styles;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Assassins open from stealth, as players do (owner 2026-11-05; Prima: Backstab, Backstab II and Perforate Artery, and
    /// the chains after them, are the class core of the Infiltrator, Shadowblade and Nightshade). Before, no bot ever used a
    /// stealth style: they were not eligible, and the brain revealed a bot before its first swing.
    ///
    /// When an assassin with a stealth style picks a target that is not attacking it, it hides (the same rules as a
    /// player's Stealth: CompanionStealth.MayHide), walks at stealth speed to the target's back (Backstab, Backstab II) or
    /// front (Perforate Artery, not on a monster facing a groupmate), faces it and opens with that style. The swing
    /// reveals it (WeaponAction), as it does a player. It gives up on a target after 12 seconds, or at once when it cannot
    /// hide, and then attacks normally. A hired companion's "Open from stealth" AI option can switch it off.
    /// </summary>
    public partial class BotBrain
    {
        // Q31 (veteran review 2026-11-11): patience like a player's - open only on a still or slow target, re-plan the
        // route at most once a second, walk straight inside 300 units and give up after 12 s (it re-planned on every
        // think for up to 25 s: 15-25 route requests per opener)
        public const long StealthOpenerGiveUpMilliseconds = 12_000;
        public const long StealthOpenerRepathMilliseconds = 1_000;
        public const int StealthOpenerStraightWalk = 300;
        public const int StealthOpenerMaxTargetSpeed = 100;
        private const int StealthOpenerStandOff = 70;
        private GameLiving _openerTarget;
        private GameLiving _openerGaveUpOn;
        private long _openerSince;
        private long _openerNextPath;

        /// <summary>Pure (Q31): a stealth approach starts only on a target standing still or walking slowly.</summary>
        public static bool OpenerTargetSlowEnough(int targetSpeed) => targetSpeed <= StealthOpenerMaxTargetSpeed;

        /// <summary>Pure (Q31): how the bot moves to its opener spot this turn - 0 keep going, 1 a straight walk (close),
        /// 2 a new route (far, at most once a second).</summary>
        public static int OpenerMove(float distanceToSpot, long now, long nextPathTick) =>
            distanceToSpot <= StealthOpenerStraightWalk ? 1 : now >= nextPathTick ? 2 : 0;

        /// <summary>True while this bot is hidden on its way to (or about to swing) a stealth opener.</summary>
        public bool StealthOpenerActive => _openerTarget != null && Body?.IsStealthed == true;

        private static bool IsStealthOpenerStyle(Style style) => style != null && style.StealthRequirement;

        /// <summary>
        /// An assassin that opens its fights from stealth: it has Stealth, knows a stealth style at its level and its
        /// player has not switched the opener off. The solo ranged pull leaves such a bot alone (its thrown knife or
        /// crossbow shot aggroed the monster first, so the opener never happened - 2026-11-05 audit).
        /// </summary>
        public static bool OpensFromStealth(GameBot bot) =>
            Classic165Mode.IsOn && bot?.CharacterClass != null && bot.Styles != null &&
            BotPoisonSupply.IsAssassin((eCharacterClass)bot.CharacterClass.ID) && CompanionStealth.CanStealth(bot) &&
            CompanionOptions165.Choice(bot, "stealthopen") != 1 &&
            bot.Styles.Any(style => IsStealthOpenerStyle(style) && style.Level <= bot.Level);

        private bool StealthOpenerWanted(GameLiving target)
        {
            GameBot bot = BotBody;
            if (!OpensFromStealth(bot) || target == null || !target.IsAlive || target == _openerGaveUpOn ||
                bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance || GameRelic.IsPlayerCarryingRelic(bot) ||
                target.TargetObject == bot || target.CurrentRegionID != bot.CurrentRegionID ||
                !bot.IsWithinRadius(target, 2_500))
                return false;
            // hiding needs a moment out of combat; already hidden, it carries on
            return bot.IsStealthed || !bot.InCombat && !HasAggro;
        }

        /// <summary>The opener for this approach: Perforate Artery (front) on a monster not fighting a groupmate, else the
        /// best Backstab (back). Null when none fits the weapon in hand.</summary>
        private Style ChooseStealthOpener(GameLiving target)
        {
            GameBot bot = BotBody;
            Style[] known = bot.Styles.Where(style => IsStealthOpenerStyle(style) && style.Level <= bot.Level &&
                    BotMeleeStylePolicy.MatchesWeapon(style, bot.ActiveWeapon, bot.Inventory?.GetItem(eInventorySlot.LeftHandWeapon),
                        bot.ActiveWeaponSlot))
                .ToArray();
            bool facingSomeoneElse = target.TargetObject is GameLiving victim && victim != bot && victim.IsAlive;
            return known
                .Where(style => !facingSomeoneElse ||
                                (Style.eOpeningPosition)style.OpeningRequirementValue != Style.eOpeningPosition.Front)
                .OrderByDescending(style => style.Level)
                .FirstOrDefault();
        }

        /// <summary>Runs the stealth approach; true while it owns the bot's turn.</summary>
        private bool TryStealthOpener(GameLiving target)
        {
            if (!StealthOpenerWanted(target))
            {
                _openerTarget = null;
                return false;
            }
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (_openerTarget != target)
            {
                // a running monster is not stalked: fight it the ordinary way
                if (!bot.IsStealthed && !OpenerTargetSlowEnough(target.CurrentSpeed))
                {
                    _openerTarget = null;
                    return false;
                }
                _openerTarget = target;
                _openerSince = now;
                _openerNextPath = 0;
            }
            // the opener is queued and the swing not yet done: leave it be
            if (bot.IsStealthed && bot.attackComponent.AttackState && bot.TargetObject == target &&
                IsStealthOpenerStyle(bot.styleComponent.NextCombatStyle))
                return true;
            if (now - _openerSince > StealthOpenerGiveUpMilliseconds)
                return GiveUp();

            if (!bot.IsStealthed)
            {
                if (!CompanionStealth.MayHide(bot))
                    return GiveUp();
                bot.Stealth(true);
                if (!bot.IsStealthed)
                    return GiveUp();
            }

            Style opener = ChooseStealthOpener(target);
            if (opener == null)
                return GiveUp();
            if (bot.attackComponent.AttackState)
                bot.StopAttack();

            bool back = (Style.eOpeningPosition)opener.OpeningRequirementValue == Style.eOpeningPosition.Back;
            ushort heading = back ? (ushort)((target.Heading + 2048) % 4096) : target.Heading;
            Point2D spot = target.GetPointFromHeading(heading, StealthOpenerStandOff);
            Vector3 destination = new(spot.X, spot.Y, target.Z);
            float toSpot = Vector2.Distance(new(bot.X, bot.Y), new(spot.X, spot.Y));
            if (toSpot > 35)
            {
                switch (OpenerMove(toSpot, now, _openerNextPath))
                {
                    case 1:
                        bot.WalkTo(destination, bot.MaxSpeed);
                        break;
                    case 2:
                        _openerNextPath = now + StealthOpenerRepathMilliseconds;
                        bot.PathTo(destination, bot.MaxSpeed);
                        break;
                }
                return true;
            }

            bot.StopMoving();
            bot.TurnTo(target);
            if (!StyleProcessor.CanUseStyle(null, bot, opener, BotMeleeStylePolicy.Weapon(bot, opener)))
                return true;   // turned or moved: the next turn walks to the new spot
            bot.styleComponent.NextCombatStyle = opener;
            bot.StartAttack(target);
            return true;

            bool GiveUp()
            {
                _openerGaveUpOn = target;
                _openerTarget = null;
                return false;
            }
        }
    }
}
