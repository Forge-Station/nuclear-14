using Content.Shared._NC.Trade;
using Robust.Shared.Map;


namespace Content.Server._NC.Trade;


public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryResolveObjectiveSpawnCoordinates(
        EntityUid store,
        ContractObjectiveConfigData config,
        out EntityCoordinates coordinates,
        bool fallbackToStore = true
    ) =>
        TryResolveObjectiveSpawnCoordinates(store, config.SpawnPoint, out coordinates, fallbackToStore);

    private bool TryResolveObjectiveDropoffCoordinates(
        EntityUid store,
        ContractObjectiveConfigData config,
        out EntityCoordinates coordinates,
        bool fallbackToStore = false
    ) =>
        TryResolveObjectiveSpawnCoordinates(store, config.DropoffPoint, out coordinates, fallbackToStore);

    private static bool HasConfiguredObjectiveDropoff(ContractObjectiveConfigData config) =>
        config.DropoffPoint != null;

    private bool TryResolveObjectiveSpawnCoordinates(
        EntityUid store,
        ContractPointSelectorPrototype? selector,
        out EntityCoordinates coordinates,
        bool fallbackToStore = true
    )
    {
        GetObjectiveSpawnFallback(store, out var storeXform, out coordinates);

        if (storeXform == null)
            return false;

        var effectiveSelector = selector ?? new ContractPointSelectorPrototype();
        if (effectiveSelector.Type == ContractPointSelectorType.Store)
            return coordinates != EntityCoordinates.Invalid;

        if (TryPickObjectiveSpawnCoordinate(storeXform.MapID, effectiveSelector, out var selectedCoordinates))
        {
            coordinates = selectedCoordinates;
            return true;
        }

        if (fallbackToStore)
        {
            Sawmill.Warning(
                $"[Contracts] Spawn point selector '{DescribeContractPointSelector(effectiveSelector)}' not found for {ToPrettyString(store)}. Fallback to store coordinates.");
            return coordinates != EntityCoordinates.Invalid;
        }

        Sawmill.Warning(
            $"[Contracts] Spawn point selector '{DescribeContractPointSelector(effectiveSelector)}' not found for {ToPrettyString(store)}.");
        return false;
    }

    private void GetObjectiveSpawnFallback(
        EntityUid store,
        out TransformComponent? storeXform,
        out EntityCoordinates coordinates
    )
    {
        if (TryComp(store, out storeXform))
        {
            coordinates = storeXform.Coordinates;
            return;
        }

        coordinates = EntityCoordinates.Invalid;
    }

    private bool TryPickObjectiveSpawnCoordinate(
        MapId storeMap,
        ContractPointSelectorPrototype selector,
        out EntityCoordinates coordinates
    )
    {
        coordinates = EntityCoordinates.Invalid;

        return selector.Type switch
        {
            ContractPointSelectorType.Store => false,
            ContractPointSelectorType.MarkerId => TryPickObjectiveSpawnCoordinateById(
                storeMap,
                selector.Id,
                out coordinates),
            ContractPointSelectorType.MarkerGroup => TryPickObjectiveSpawnCoordinateByGroup(
                storeMap,
                selector.Id,
                out coordinates),
            ContractPointSelectorType.Weighted => TryPickObjectiveSpawnCoordinateWeighted(
                storeMap,
                selector.Options,
                out coordinates),
            _ => false
        };
    }

    private bool TryPickObjectiveSpawnCoordinateWeighted(
        MapId storeMap,
        IReadOnlyList<WeightedContractPointOptionEntry>? options,
        out EntityCoordinates coordinates
    )
    {
        coordinates = EntityCoordinates.Invalid;

        if (options == null || options.Count == 0)
            return false;

        var totalWeight = 0;
        var found = false;

        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            if (option.Weight <= 0 || !IsContractPointOptionUsable(option))
                continue;

            if (!TryPickObjectiveSpawnCoordinate(storeMap, option, out var candidate))
                continue;

            totalWeight += option.Weight;
            if (_random.Next(totalWeight) >= option.Weight)
                continue;

            coordinates = candidate;
            found = true;
        }

        return found;
    }

    private bool TryPickObjectiveSpawnCoordinate(
        MapId storeMap,
        in WeightedContractPointOptionEntry option,
        out EntityCoordinates coordinates
    )
    {
        coordinates = EntityCoordinates.Invalid;

        return option.Type switch
        {
            ContractPointSelectorType.Store => false,
            ContractPointSelectorType.MarkerId => TryPickObjectiveSpawnCoordinateById(
                storeMap,
                option.Id,
                out coordinates),
            ContractPointSelectorType.MarkerGroup => TryPickObjectiveSpawnCoordinateByGroup(
                storeMap,
                option.Id,
                out coordinates),
            _ => false
        };
    }

    private bool TryPickObjectiveSpawnCoordinateById(
        MapId storeMap,
        string id,
        out EntityCoordinates coordinates
    )
    {
        coordinates = EntityCoordinates.Invalid;

        if (string.IsNullOrWhiteSpace(id))
            return false;

        var matches = 0;
        var found = false;

        var query = EntityQueryEnumerator<NcContractSpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out _, out var point, out var xform))
        {
            if (xform.MapID != storeMap || !string.Equals(point.Id, id, StringComparison.Ordinal))
                continue;

            matches++;
            if (_random.Next(matches) != 0)
                continue;

            coordinates = xform.Coordinates;
            found = true;
        }

        return found;
    }

    private bool TryPickObjectiveSpawnCoordinateByGroup(
        MapId storeMap,
        string groupId,
        out EntityCoordinates coordinates
    )
    {
        coordinates = EntityCoordinates.Invalid;

        if (string.IsNullOrWhiteSpace(groupId))
            return false;

        var matches = 0;
        var found = false;

        var query = EntityQueryEnumerator<NcContractSpawnPointComponent, TransformComponent>();
        while (query.MoveNext(out _, out var point, out var xform))
        {
            if (xform.MapID != storeMap || !ContractPointHasGroup(point, groupId))
                continue;

            matches++;
            if (_random.Next(matches) != 0)
                continue;

            coordinates = xform.Coordinates;
            found = true;
        }

        return found;
    }

    private static bool ContractPointHasGroup(NcContractSpawnPointComponent point, string groupId)
    {
        var groups = point.Groups;
        if (groups.Count == 0)
            return false;

        for (var i = 0; i < groups.Count; i++)
            if (string.Equals(groups[i], groupId, StringComparison.Ordinal))
                return true;

        return false;
    }

    private static string DescribeContractPointSelector(ContractPointSelectorPrototype selector) =>
        selector.Type switch
        {
            ContractPointSelectorType.Store => "Store",
            ContractPointSelectorType.MarkerId => $"MarkerId:{selector.Id}",
            ContractPointSelectorType.MarkerGroup => $"MarkerGroup:{selector.Id}",
            ContractPointSelectorType.Weighted => $"Weighted[{selector.Options.Count}]",
            _ => "Unknown"
        };
}
