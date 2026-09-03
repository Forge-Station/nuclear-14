using Content.Server.Silicons.Laws;
using Content.Shared._Forge.Silicons.RandomLaws;
using Content.Shared.Silicons.Laws.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Forge.Silicons.RandomLaws;

public sealed class RandomSiliconLawsSystem : EntitySystem
{
    [Dependency] private readonly SiliconLawSystem _laws = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private GlitchedLawGenerator _generator = default!;

    public override void Initialize()
    {
        base.Initialize();

        _generator = new GlitchedLawGenerator(_proto, _random);

        SubscribeLocalEvent<RandomSiliconLawsComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<RandomSiliconLawsComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<SiliconLawProviderComponent>(ent))
            return;

        var laws = _generator.GenerateLaws(ent.Comp.MinLaws, ent.Comp.MaxLaws);
        _laws.SetLaws(laws, ent);
    }
}
