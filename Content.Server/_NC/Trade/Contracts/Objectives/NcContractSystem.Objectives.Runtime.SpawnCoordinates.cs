using Content.Shared._NC.Trade;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private bool TryResolveObjectiveSpawnCoordinates(
        EntityUid store,
        ContractObjectiveConfigData config,
        out EntityCoordinates coordinates,
        bool fallbackToStore = true
    )
    {
        return TryResolveObjectiveSpawnCoordinates(
            store,
            config.SpawnPointTag,
            config.SpawnPointTags,
            out coordinates,
            fallbackToStore);
    }

    private bool TryResolveObjectiveDropoffCoordinates(
        EntityUid store,
        ContractObjectiveConfigData config,
        out EntityCoordinates coordinates,
        bool fallbackToStore = false
    )
    {
        return TryResolveObjectiveSpawnCoordinates(
            store,
            config.DropoffPointTag,
            config.DropoffPointTags,
            out coordinates,
            fallbackToStore);
    }

    private static bool HasConfiguredObjectiveDropoff(ContractObjectiveConfigData config)
    {
        return !string.IsNullOrWhiteSpace(config.DropoffPointTag) ||
               config.DropoffPointTags is { Count: > 0 };
    }

    private bool TryResolveObjectiveSpawnCoordinates(
        EntityUid store,
        string? spawnTag,
        out EntityCoordinates coordinates,
        bool fallbackToStore = true
    )
    {
        return TryResolveObjectiveSpawnCoordinates(store, spawnTag, null, out coordinates, fallbackToStore);
    }

    private bool TryResolveObjectiveSpawnCoordinates(
        EntityUid store,
        string? spawnTag,
        IReadOnlyList<WeightedTagEntry>? weightedSpawnTags,
        out EntityCoordinates coordinates,
        bool fallbackToStore = true
    )
    {
        GetObjectiveSpawnFallback(store, out var storeXform, out coordinates);

        var selectedSpawnTag = ResolveObjectiveSpawnTag(storeXform?.MapID ?? MapId.Nullspace, spawnTag, weightedSpawnTags);

        if (string.IsNullOrWhiteSpace(selectedSpawnTag))
            return fallbackToStore && coordinates != EntityCoordinates.Invalid;

        if (!_prototypes.HasIndex<TagPrototype>(selectedSpawnTag))
            return HandleMissingObjectiveSpawnTag(selectedSpawnTag, coordinates, fallbackToStore);

        if (storeXform == null)
            return false;

        if (TryPickObjectiveSpawnCoordinate(storeXform.MapID, selectedSpawnTag, out var selectedCoordinates))
        {
            coordinates = selectedCoordinates;
            return true;
        }

        return HandleUnavailableObjectiveSpawnTag(store, selectedSpawnTag, coordinates, fallbackToStore);
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

    private string? ResolveObjectiveSpawnTag(
        MapId mapId,
        string? spawnTag,
        IReadOnlyList<WeightedTagEntry>? weightedSpawnTags
    )
    {
        var selectedSpawnTag = spawnTag;
        if (weightedSpawnTags is not { Count: > 0 })
            return selectedSpawnTag;

        var weightedTag = PickAvailableObjectiveSpawnTag(mapId, weightedSpawnTags);
        if (!string.IsNullOrWhiteSpace(weightedTag))
            selectedSpawnTag = weightedTag;

        return selectedSpawnTag;
    }

    private bool HandleMissingObjectiveSpawnTag(
        string selectedSpawnTag,
        EntityCoordinates fallbackCoordinates,
        bool fallbackToStore
    )
    {
        if (fallbackToStore)
        {
            Sawmill.Warning($"[Contracts] Spawn tag '{selectedSpawnTag}' is not defined. Fallback to store coordinates.");
            return fallbackCoordinates != EntityCoordinates.Invalid;
        }

        Sawmill.Warning($"[Contracts] Spawn tag '{selectedSpawnTag}' is not defined.");
        return false;
    }

    private bool TryPickObjectiveSpawnCoordinate(
        MapId storeMap,
        string selectedSpawnTag,
        out EntityCoordinates coordinates
    )
    {
        coordinates = EntityCoordinates.Invalid;
        var matches = 0;
        var found = false;

        var query = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (query.MoveNext(out _, out var tagComp, out var xform))
        {
            if (xform.MapID != storeMap || !_tags.HasTag(tagComp, selectedSpawnTag))
                continue;

            matches++;
            if (_random.Next(matches) != 0)
                continue;

            coordinates = xform.Coordinates;
            found = true;
        }

        return found;
    }

    private bool HandleUnavailableObjectiveSpawnTag(
        EntityUid store,
        string selectedSpawnTag,
        EntityCoordinates fallbackCoordinates,
        bool fallbackToStore
    )
    {
        if (fallbackToStore)
        {
            Sawmill.Warning(
                $"[Contracts] Spawn tag '{selectedSpawnTag}' not found on map for {ToPrettyString(store)}. Fallback to store coordinates.");
            return fallbackCoordinates != EntityCoordinates.Invalid;
        }

        Sawmill.Warning($"[Contracts] Spawn tag '{selectedSpawnTag}' not found on map for {ToPrettyString(store)}.");
        return false;
    }

    private string? PickAvailableObjectiveSpawnTag(
        MapId mapId,
        IReadOnlyList<WeightedTagEntry>? weightedSpawnTags
    )
    {
        if (weightedSpawnTags == null || weightedSpawnTags.Count == 0)
            return null;

        var totalWeight = 0;
        string? selectedTag = null;

        for (var i = 0; i < weightedSpawnTags.Count; i++)
        {
            var entry = weightedSpawnTags[i];
            if (string.IsNullOrWhiteSpace(entry.Tag) ||
                entry.Weight <= 0 ||
                !_prototypes.HasIndex<TagPrototype>(entry.Tag) ||
                !HasObjectiveSpawnTagOnMap(mapId, entry.Tag))
            {
                continue;
            }

            totalWeight += entry.Weight;
            if (_random.Next(totalWeight) < entry.Weight)
                selectedTag = entry.Tag;
        }

        return selectedTag;
    }

    private bool HasObjectiveSpawnTagOnMap(MapId mapId, string tag)
    {
        var query = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (query.MoveNext(out _, out var tagComp, out var xform))
        {
            if (xform.MapID == mapId && _tags.HasTag(tagComp, tag))
                return true;
        }

        return false;
    }
}
