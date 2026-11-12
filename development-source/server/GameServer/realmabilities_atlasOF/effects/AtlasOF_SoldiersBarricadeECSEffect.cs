namespace DOL.GS.Effects
{
    public class SoldiersBarricadeECSEffect : ECSGameAbilityEffect
    {
        public SoldiersBarricadeECSEffect(in ECSGameEffectInitParams initParams)
            : base(initParams)
        {
            EffectType = eEffect.SoldiersBarricade;
        }

        public override ushort Icon { get { return 4241; } }
        public override string Name { get { return "Soldier's Barricade"; } }
        public override bool HasPositiveEffect { get { return true; } }

        // A gamebot group member gets the armor too (M8: a gamebot Armsman's Barricade covers its bot group).
        private static bool Covers(GameLiving owner) => owner is GamePlayer or GameBot;

        public override void OnStartEffect()
        {
            if (!Covers(Owner))
                return;

            Owner.OtherBonus[eProperty.ArmorFactor] += (int)Effectiveness;
            OwnerPlayer?.Out.SendUpdateWeaponAndArmorStats();
        }

        public override void OnStopEffect()
        {
            if (!Covers(Owner))
                return;

            Owner.OtherBonus[eProperty.ArmorFactor] -= (int)Effectiveness;
            OwnerPlayer?.Out.SendUpdateWeaponAndArmorStats();
        }
    }
}
