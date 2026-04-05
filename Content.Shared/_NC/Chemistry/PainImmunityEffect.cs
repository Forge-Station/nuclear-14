using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using System;

namespace Content.Server.PainImmunity.Effects
{
    public sealed partial class PainImmunityEffect : EntityEffect
    {
        [DataField("duration")]
        public float Duration = 10f;

        protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        {
            return Loc.GetString("reagent-effect-pain-immunity", ("duration", Duration));
        }

        public override void Effect(EntityEffectBaseArgs args)
        {
            var painSystem = args.EntityManager.System<PainImmunitySystem>();
            if (painSystem == null) return;
            painSystem.ApplyPainImmunity(args.TargetEntity, TimeSpan.FromSeconds(Duration));
        }
    }
}