using Content.Shared.EntityEffects;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Content.Shared.Body.Systems;

namespace Content.Server.EntityEffects.Effects;

/// <summary>
/// Basically smoke and foam reactions.
/// </summary>
[UsedImplicitly]
public sealed partial class ChemCleanBloodstream : LegacyEntityEffect
{
    [DataField]
    public float CleanseRate = 3.0f;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-chem-clean-bloodstream", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        
        var cleanseRate = CleanseRate;

        var bloodstreamSys = args.EntityManager.System<BloodstreamSystem>();

        // Forge-Change: у FlushChemicals поменялся порядок аргументов, а исключаемый
        // реагент стал необязательным.
        if (args is EntityEffectReagentArgs reagentArgs)
        {
            cleanseRate *= reagentArgs.Scale.Float();
            bloodstreamSys.FlushChemicals(args.TargetEntity, cleanseRate, reagentArgs.Reagent?.ID);
        }
        else
        {
            bloodstreamSys.FlushChemicals(args.TargetEntity, cleanseRate);
        }
    }
}
