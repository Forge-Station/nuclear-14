using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Containers;

namespace Content.Shared._Forge.Silicons.BorgBrainGuard;

public sealed class BorgBrainGuardSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgChassisComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
    }

    // Мозг борга принимает только мозг-слот: не даём ему попасть в руку (модуль-манипулятор)
    // или иной контейнер шасси. Иначе BorgSystem.OnInserted/OnRemoved переносит разум на любой
    // контейнер — борг с мозгом в руке оказался бы захвачен/перенёс бы своё сознание в выроненный мозг.
    private void OnInsertAttempt(Entity<BorgChassisComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Cancelled || args.Container.ID == ent.Comp.BrainContainerId)
            return;

        if (HasComp<BorgBrainComponent>(args.EntityUid))
            args.Cancel();
    }
}
