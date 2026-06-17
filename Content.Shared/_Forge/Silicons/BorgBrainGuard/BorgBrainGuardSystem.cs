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

    // Мозг пускаем только в мозг-слот: иначе апстримный BorgSystem перенёс бы разум на любой контейнер (рука и т.п.).
    private void OnInsertAttempt(Entity<BorgChassisComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Cancelled || args.Container.ID == ent.Comp.BrainContainerId)
            return;

        if (HasComp<BorgBrainComponent>(args.EntityUid))
            args.Cancel();
    }
}
