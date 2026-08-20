using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared._Shitmed.Body.Events;

namespace Content.Server._Forge.Body;

/// <summary>
///     Переименовывает отрубленную голову, добавляя к ней имя владельца тела,
///     чтобы игроки могли отличить головы своих напарников от вражеских.
/// </summary>
public sealed class SeveredPartNamingSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();

        // BodyPartDroppedEvent кидается направленно (RaiseLocalEvent(body, ref ev) без broadcast: true),
        // поэтому глобальная подписка никогда не вызывается.
        // Событие долетает только до directed-подписчиков на компонентах сущности-тела.
        SubscribeLocalEvent<BodyComponent, BodyPartDroppedEvent>(OnPartDropped);
    }

    private void OnPartDropped(Entity<BodyComponent> body, ref BodyPartDroppedEvent args)
    {
        var part = args.Part;

        // Интересует только голова.
        if (part.Comp.PartType != BodyPartType.Head)
            return;

        if (TerminatingOrDeleted(part) || TerminatingOrDeleted(body))
            return;

        var ownerName = Name(body);
        if (string.IsNullOrWhiteSpace(ownerName))
            return;

        _meta.SetEntityName(part, Loc.GetString("forge-severed-head-name", ("name", ownerName)));
    }
}