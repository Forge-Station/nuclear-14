using Content.Shared._Forge.Paper;
using Robust.Shared.Random;

namespace Content.Server._Forge.Paper;

/// <summary>Assign a persistent sheet identity once, including copies spawned outside a printer.</summary>
public sealed class PaperSurfaceSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PaperSurfaceComponent, ComponentInit>(OnInit);
    }
    private void OnInit(EntityUid uid, PaperSurfaceComponent component, ComponentInit args)
    {
        if (component.Appearance.Seed == 0)
        {
            component.Appearance.Seed = _random.Next(1, int.MaxValue);
            Dirty(uid, component);
        }
    }
}
