// Forge-Change
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared._RMC14.Roles.Ranks;

public abstract class SharedRankSystem : EntitySystem
{
    [Dependency] private readonly SharedIdCardSystem _idCards = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RankComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<IdExaminableComponent, ExaminedEvent>(OnIdentityExamined);
    }

    private void OnExamined(Entity<RankComponent> ent, ref ExaminedEvent args)
    {
        AddRankExamine(ent.Owner, "rmc14-rank-card-examine", ref args);
    }

    private void OnIdentityExamined(Entity<IdExaminableComponent> ent, ref ExaminedEvent args)
    {
        if (TryGetRankIdCard(ent.Owner, out var idCard))
            AddRankExamine(idCard.Owner, "rmc14-rank-component-examine", ref args);
    }

    private void AddRankExamine(EntityUid rankHolder, LocId message, ref ExaminedEvent args)
    {
        var rank = GetRankString(rankHolder);
        if (rank == null)
            return;

        using (args.PushGroup(nameof(SharedRankSystem), 1))
        {
            args.PushMarkup(Loc.GetString(message,
                ("user", args.Examined),
                ("rank", rank)));
        }
    }

    /// <summary>
    /// Finds the ID card currently equipped in an entity's ID slot, including a card inside a PDA.
    /// </summary>
    protected bool TryGetRankIdCard(EntityUid uid, out Entity<IdCardComponent> idCard)
    {
        if (_inventory.TryGetSlotEntity(uid, "id", out var idSlot) &&
            _idCards.TryGetIdCard(idSlot.Value, out idCard))
        {
            return true;
        }

        idCard = default;
        return false;
    }

    public void SetRank(EntityUid uid, RankPrototype rank)
    {
        SetRank(uid, rank.ID);
    }

    public void SetRank(EntityUid uid, ProtoId<RankPrototype> rank)
    {
        var component = EnsureComp<RankComponent>(uid);
        component.Rank = rank;
        Dirty(uid, component);
    }

    public RankPrototype? GetRank(EntityUid uid)
    {
        return TryComp<RankComponent>(uid, out var component)
            ? GetRank(component)
            : null;
    }

    public RankPrototype? GetRank(RankComponent component)
    {
        return _prototypes.TryIndex(component.Rank, out var rank)
            ? rank
            : null;
    }

    public string? GetRankString(EntityUid uid)
    {
        var rank = GetRank(uid);
        if (rank == null)
            return null;

        return Loc.GetString(rank.Name);
    }
}
