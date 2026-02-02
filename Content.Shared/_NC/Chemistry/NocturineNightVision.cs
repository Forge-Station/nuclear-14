using Content.Shared.Actions;
using Content.Shared.Chemistry.Components;
using Content.Shared.EntityEffects;
using Content.Shared.Overlays.Switchable;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.Chemistry.Effects;

[DataDefinition]
public sealed partial class NocturineNightVision : EntityEffect
{
    [DataField("durationSeconds")]
    public float DurationSeconds = 2.0f;

    [DataField("color")]
    public Color NightVisionColor = Color.Green;

    protected override string ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager sys)
    {
        return Loc.GetString("reagent-effect-guidebook-nocturine-night-vision", ("time", DurationSeconds));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (args is not EntityEffectReagentArgs)
            return;

        var entMan = args.EntityManager;
        var uid = args.TargetEntity;

        if (!entMan.EntityExists(uid))
            return;

        var now = IoCManager.Resolve<IGameTiming>().CurTime;

        var meta = entMan.EnsureComponent<NocturineNightVisionMetabolismComponent>(uid);
        meta.ExpiresAt = now + TimeSpan.FromSeconds(DurationSeconds);

        var hadNightVision = entMan.TryGetComponent(uid, out NightVisionComponent? nv);

        if (hadNightVision)
        {
            if (nv!.IsEquipment)
                return;

            if (!meta.SavedOriginal)
            {
                meta.OriginalIsActive = nv.IsActive;
                meta.OriginalColor = nv.Color;
                meta.SavedOriginal = true;
            }
        }
        else
        {
            nv = entMan.EnsureComponent<NightVisionComponent>(uid);
            meta.AddedNightVision = true;
            nv.ToggleAction = null;

            if (nv.ToggleActionEntity != null)
            {
                var actions = IoCManager.Resolve<IEntitySystemManager>().GetEntitySystem<SharedActionsSystem>();
                actions.RemoveAction(uid, nv.ToggleActionEntity);
                nv.ToggleActionEntity = null;
            }
        }

        var dirty = false;

        if (!nv!.IsActive)
        {
            nv.IsActive = true;
            dirty = true;
        }

        if (nv.Color != NightVisionColor)
        {
            nv.Color = NightVisionColor;
            dirty = true;
        }

        if (dirty)
            entMan.Dirty(uid, nv);
    }
}
