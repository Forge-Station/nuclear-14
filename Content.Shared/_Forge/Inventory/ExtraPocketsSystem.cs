using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Inventory;

/// <summary>
///     Appends the slots from the "human_extra_pockets" inventory template to an entity with
///     <see cref="ExtraPocketsComponent"/>. Runs on both client and server so the UI stays in sync.
/// </summary>
public sealed partial class ExtraPocketsSystem : EntitySystem
{
    private const string ExtraPocketsTemplate = "human_extra_pockets";

    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExtraPocketsComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, ExtraPocketsComponent component, ComponentStartup args)
    {
        if (!_prototype.TryIndex<InventoryTemplatePrototype>(ExtraPocketsTemplate, out var template))
            return;

        _inventory.AddSlots(uid, template.Slots);
    }
}
