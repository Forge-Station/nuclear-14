using Content.Shared._Forge.Warfront;
using Content.Shared._Forge.Warfront.FactionPoints;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Server._Forge.Warfront.FactionPoints;

public sealed class FactionPointsSystem : EntitySystem
{
    private readonly Dictionary<WarfrontFaction, EntityUid> _accounts = new();

    [PublicAPI]
    public int GetBalance(WarfrontFaction faction)
    {
        return Comp<FactionPointsComponent>(EnsureAccount(faction)).Balance;
    }

    [PublicAPI]
    public void AddPoints(WarfrontFaction faction, int amount)
    {
        var comp = Comp<FactionPointsComponent>(EnsureAccount(faction));
        comp.Balance += amount;
        RaiseLocalEvent(new FactionPointsChangedEvent(faction, comp.Balance));
    }

    [PublicAPI]
    public bool TrySpendPoints(WarfrontFaction faction, int cost)
    {
        var comp = Comp<FactionPointsComponent>(EnsureAccount(faction));
        if (comp.Balance < cost)
            return false;

        comp.Balance -= cost;
        RaiseLocalEvent(new FactionPointsChangedEvent(faction, comp.Balance));
        return true;
    }

    private EntityUid EnsureAccount(WarfrontFaction faction)
    {
        if (_accounts.TryGetValue(faction, out var uid) && Exists(uid))
            return uid;

        var query = EntityQueryEnumerator<FactionPointsComponent>();
        while (query.MoveNext(out var existingUid, out var existingComp))
        {
            if (existingComp.Faction != faction)
                continue;

            _accounts[faction] = existingUid;
            return existingUid;
        }

        var newUid = Spawn(null, MapCoordinates.Nullspace);
        var newComp = EnsureComp<FactionPointsComponent>(newUid);
        newComp.Faction = faction;
        _accounts[faction] = newUid;
        return newUid;
    }
}
