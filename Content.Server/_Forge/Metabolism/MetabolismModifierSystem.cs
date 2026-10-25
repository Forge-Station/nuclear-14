using Content.Server.Body.Systems;
using Content.Server.Medical;

namespace Content.Server._Forge.Metabolism;

public sealed partial class MetabolismModifierSystem : EntitySystem
{
    [Dependency] private readonly VomitSystem _vomit = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MetabolismModifierComponent, TryMetabolizeReagent>(OnMetabolize);
    }

    private void OnMetabolize(EntityUid uid, MetabolismModifierComponent component, ref TryMetabolizeReagent args)
    {
        var id = args.Prototype.ID;

        foreach (var reagent in component.Neutralize)
        {
            if (reagent.Id != id)
                continue;

            args.QuantityMultiplier = 0f;
            args.Scale = 0f;
            return;
        }

        foreach (var reagent in component.Vomit)
        {
            if (reagent.Id != id)
                continue;

            _vomit.Vomit(uid);
            args.QuantityMultiplier = 0f;
            args.Scale = 0f;
            return;
        }
    }
}
