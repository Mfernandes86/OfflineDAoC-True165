using DOL.GS.PacketHandler;
using DOL.Language;

namespace DOL.GS
{
    public class RapidFireECSGameEffect : ECSGameAbilityEffect
    {
        public RapidFireECSGameEffect(in ECSGameEffectInitParams initParams)
            : base(initParams)
        {
            EffectType = eEffect.RapidFire;
        }


        public override ushort Icon { get { return 484; } }
        // a gamebot Ranger uses it too (M8): no player client to translate for
        public override string Name { get { return OwnerPlayer != null ? LanguageMgr.GetTranslation(OwnerPlayer.Client, "Effects.RapidFireEffect.Name") : "Rapid Fire"; } }
        public override bool HasPositiveEffect { get { return true; } }

        public override void OnStartEffect()
        {
            if (OwnerPlayer != null)
                OwnerPlayer.Out.SendMessage(LanguageMgr.GetTranslation(OwnerPlayer.Client, "Effects.RapidFireEffect.YouSwitchRFMode"), eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
        public override void OnStopEffect()
        {

        }
    }
}
