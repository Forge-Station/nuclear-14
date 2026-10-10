using Content.Shared.Examine;
using Content.Shared.Lathe;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Crafting;

public sealed class BlueprintExamineSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedLatheSystem _lathe = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BlueprintComponent, ExaminedEvent>(OnExamine);
    }

    private void OnExamine(EntityUid uid, BlueprintComponent component, ExaminedEvent args)
    {
        if (component.Recipes.Count == 0)
        {
            args.PushMarkup(Loc.GetString("blueprint-examine-none"));
            return;
        }

        if (!_proto.TryIndex(component.Recipes[0], out var recipe))
            return;

        var message = Loc.GetString("blueprint-examine", ("result", _lathe.GetRecipeName(recipe)));

        if (component.Recipes.Count > 1)
            message += " " + Loc.GetString("blueprint-examine-more");

        args.PushMarkup(message);
    }
}
