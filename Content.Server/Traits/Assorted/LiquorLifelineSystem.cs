using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Body;
using Content.Shared.Metabolism;
using Content.Shared.Body.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.Traits.Assorted;

public sealed class LiquorLifelineSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _bodySystem = default!; // Forge-Change

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LiquorLifelineComponent, ComponentInit>(OnSpawn);
    }

    private void OnSpawn(Entity<LiquorLifelineComponent> entity, ref ComponentInit args)
    {
        if (!TryComp<BodyComponent>(entity, out var body))
            return;

        if (!_bodySystem.TryGetBodyOrganComponents<MetabolizerComponent>(entity, out var metabolizers, body))
            return;

        // Forge-Change: у метаболизаторов больше нет групп вроде "Alcohol" - реагенты сами
        // говорят, какой тип метаболизатора их обрабатывает. Поэтому тип просто добавляется
        // всем органам, которые вообще что-то метаболизируют; сработает он только на тех
        // реагентах, которые его ждут.
        foreach (var (metabolizer, _) in metabolizers)
        {
            metabolizer.MetabolizerTypes ??= new HashSet<ProtoId<MetabolizerTypePrototype>>();
            metabolizer.MetabolizerTypes.Add("LiquorLifeline");
        }
    }
}
