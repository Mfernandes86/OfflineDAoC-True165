using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS
{
    public class StagECSGameEffect : ECSGameAbilityEffect
    {
        public StagECSGameEffect(in ECSGameEffectInitParams initParams, int level)
            : base(initParams)
        {
            m_level = level;
            EffectType = eEffect.Stag;
		}

        /// <summary>
		/// The amount of max health gained
		/// </summary>
		protected int m_amount;

        protected ushort m_originalModel;

        protected int m_level;

        public override ushort Icon { get { return 480; } }
        public override string Name { get { return OwnerPlayer != null ? LanguageMgr.GetTranslation(OwnerPlayer.Client, "Effects.StagEffect.Name") : "Stag"; } }
        public override bool HasPositiveEffect { get { return true; } }

        /// <summary>The Stag form's model: the Lurikeen Stag Man for a lurikeen, the Stag Man for every other race.</summary>
        public static ushort FormModel(int race) => race == (int)eRace.Lurikeen ? (ushort)13 : (ushort)4;

        /// <summary>
        /// Who takes the form: a player, and a gamebot or hired companion Hero (a GameBot is player-like but is not a
        /// GamePlayer, so it kept its own figure through the whole effect; the Berserker's Vendo form already covered
        /// both). Their race, or null for anything else: a monster keeps its model.
        /// </summary>
        public static int? FormRace(GameLiving owner) => owner switch
        {
            GamePlayer player => player.Race,
            GameBot bot => bot.Race,
            _ => null,
        };

        public override void OnStartEffect()
        {
			m_originalModel = Owner.Model;

			if (FormRace(Owner) is int race)
				Owner.Model = FormModel(race);


			double m_amountPercent = (m_level + 0.5 + Util.RandomDouble()) / 10; //+-5% random
			if (OwnerPlayer != null)
				m_amount = (int)(OwnerPlayer.CalculateMaxHealth(OwnerPlayer.Level, OwnerPlayer.GetModified(eProperty.Constitution)) * m_amountPercent);
			else m_amount = (int)(Owner.MaxHealth * m_amountPercent);

			Owner.BaseBuffBonusCategory[eProperty.MaxHealth] += m_amount;
			Owner.Health += (int)(Owner.GetModified(eProperty.MaxHealth) * m_amountPercent);
			if (Owner.Health > Owner.MaxHealth) Owner.Health = Owner.MaxHealth;

			Owner.Emote(eEmote.StagFrenzy);

			if (OwnerPlayer != null)
			{
				OwnerPlayer.Out.SendUpdatePlayer();
				OwnerPlayer.Out.SendMessage(LanguageMgr.GetTranslation(OwnerPlayer.Client, "Effects.StagEffect.HuntsSpiritChannel"), eChatType.CT_YouHit, eChatLoc.CL_SystemWindow);
			}
		}
        public override void OnStopEffect()
        {
			Owner.Model = m_originalModel;
			
			Owner.BaseBuffBonusCategory[eProperty.MaxHealth] -= m_amount;
			if (Owner.IsAlive && Owner.Health > Owner.MaxHealth)
				Owner.Health = Owner.MaxHealth;

			if (OwnerPlayer != null)
			{
				OwnerPlayer.Out.SendUpdatePlayer();
				// there is no animation on end of the effect
				OwnerPlayer.Out.SendMessage(LanguageMgr.GetTranslation(OwnerPlayer.Client, "Effects.StagEffect.YourHuntsSpiritEnds"), eChatType.CT_YouHit, eChatLoc.CL_SystemWindow);
			}
		}
    }
}
