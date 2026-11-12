using DOL.AI.Brain;
using DOL.GS.RealmAbilities;
using DOL.GS.Spells;

namespace DOL.GS
{
    /// <summary>
    /// Juggernaut for a gamebot or companion Cabalist (owner 2026-11-11: "if it was in 1.65 add it to work alongside their
    /// pet"). Prima's Cabalist realm abilities: "Juggernaut - 30 min. - 14 - Summons powerful pet for 4 minutes." The
    /// player's ability refuses while a pet is out and takes the pet slot; a bot always keeps its simulacrum (its AI
    /// resummons it), so its Juggernaut is a second, timed pet that is not the controlled pet: the same spell (90801, the
    /// arcanium simulacrum at the caster's own level), the same 4 minutes and the same 30-minute reuse. It follows its
    /// owner and fights what the simulacrum fights (else what its owner fights), so it never picks a target of its own
    /// and never breaks a mez the simulacrum is kept off.
    /// Cost: nothing until used; then one pet for four minutes with the ordinary pet think.
    /// </summary>
    public static class BotJuggernaut165
    {
        public const int SpellId = 90801;
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(BotJuggernaut165));

        /// <summary>
        /// Pure: a long-reuse summon is for a fight that matters - an enemy realm, the bot itself under half health, or
        /// its group taking burst damage.
        /// </summary>
        public static bool Wanted(bool inCombat, bool controlled, bool enemyRealmFight, int healthPercent, bool groupBurst) =>
            inCombat && !controlled && (enemyRealmFight || healthPercent < 50 || groupBurst);

        /// <summary>Pure: the Juggernaut stays only while its owner is alive, in the world and in its region.</summary>
        public static bool OwnerHolds(bool ownerAlive, bool ownerInWorld, bool sameRegion) => ownerAlive && ownerInWorld && sameRegion;

        /// <summary>
        /// Pure: whose target the Juggernaut takes. The simulacrum's while it attacks one (its orders already keep it off
        /// mezzed monsters), else its owner's while the owner fights; nothing otherwise.
        /// </summary>
        public static T AssistTarget<T>(T mainPetTarget, bool mainPetAttacking, T ownerTarget, bool ownerFighting) where T : class =>
            mainPetAttacking && mainPetTarget != null ? mainPetTarget : ownerFighting ? ownerTarget : null;

        /// <summary>Summons the Juggernaut beside the bot's own pet. False when the spell or its template is missing.</summary>
        public static bool Summon(GameBot bot)
        {
            Spell spell = SkillBase.GetSpellByID(SpellId);
            if (bot == null || !bot.IsAlive || spell == null)
                return false;
            var handler = new SummonJuggernautBeside(bot, spell, GlobalSpellsLines.RealmSpellsSpellLine);
            handler.ApplyEffectOnTarget(bot);
            if (handler.Pet == null)
                return false;
            foreach (GamePlayer player in bot.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                player.Out.SendSpellEffectAnimation(bot, bot, spell.ClientEffect, 0, false, 1);
            FeatureCounters165.Add("bot_juggernaut");
            Log.Info($"BOT_JUGGERNAUT bot=\"{bot.Name}\" level={bot.Level} pet=\"{handler.Pet.Name}\" petLevel={handler.Pet.Level} " +
                     $"health={bot.HealthPercent} region={bot.CurrentRegionID}");
            return true;
        }
    }
}

namespace DOL.GS.Spells
{
    /// <summary>
    /// The Juggernaut summon for a bot (BotJuggernaut165): the pet does not take its owner's controlled-pet slot, lasts the
    /// realm ability's four minutes and has the brain that follows the owner's fight. Built directly, never from the
    /// spell table (no SpellHandler attribute), so a player's Juggernaut is unchanged.
    /// </summary>
    public class SummonJuggernautBeside : SummonJuggernaut
    {
        public SummonJuggernautBeside(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        protected override IControlledBrain GetPetBrain(GameLiving owner) => new BotJuggernautBrain(owner);

        protected override void SetBrainToOwner(IControlledBrain brain) { }

        protected override int CalculateEffectDuration(GameLiving target) => AtlasOF_Juggernaut.duration;

        public override void OnPetReleased() { }
    }
}

namespace DOL.AI.Brain
{
    using DOL.GS;

    /// <summary>A bot's Juggernaut: a timed second pet that follows its owner and fights beside the owner's own pet.</summary>
    public class BotJuggernautBrain : JuggernautBrain
    {
        public BotJuggernautBrain(GameLiving owner) : base(owner)
        {
            IsMainPet = false;
        }

        public override void Think()
        {
            GameLiving owner = Owner;
            if (!DOL.GS.BotJuggernaut165.OwnerHolds(owner?.IsAlive == true,
                    owner?.ObjectState == DOL.GS.GameObject.eObjectState.Active, owner?.CurrentRegion == Body.CurrentRegion))
            {
                Body.Die(null);
                return;
            }
            DOL.GS.GameNPC mainPet = owner.ControlledBrain?.Body;
            DOL.GS.GameLiving enemy = DOL.GS.BotJuggernaut165.AssistTarget(mainPet?.TargetObject as DOL.GS.GameLiving,
                mainPet?.IsAttacking == true, owner.TargetObject as DOL.GS.GameLiving, owner.InCombat || owner.IsAttacking);
            if (enemy != null && enemy.IsAlive && enemy != Body && enemy != owner && enemy != mainPet &&
                DOL.GS.GameServer.ServerRules.IsAllowedToAttack(Body, enemy, true))
                Attack(enemy);
            base.Think();
        }
    }
}
