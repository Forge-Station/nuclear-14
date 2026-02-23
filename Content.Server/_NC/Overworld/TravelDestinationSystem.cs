using Content.Shared.Overworld;
using Content.Shared.Overworld.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server.Overworld;

public sealed class TravelDestinationSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly OverworldMapSystem _maps = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("overworld.destination");
    }

    public bool TryResolve(ProtoId<TravelDestinationPrototype> protoId, out EntityCoordinates destination)
    {
        destination = default;

        if (!_proto.TryIndex(protoId, out var proto))
        {
            _sawmill.Error($"TravelDestination prototype '{protoId}' not found.");
            return false;
        }

        return proto.DestinationType switch
        {
            TravelDestinationType.Static   => TryResolveStatic(proto, out destination),
            TravelDestinationType.Instance => TryResolveInstance(proto, out destination),
            _ => false
        };
    }

    private bool TryResolveStatic(TravelDestinationPrototype proto, out EntityCoordinates dest)
    {
        dest = default;

        if (string.IsNullOrWhiteSpace(proto.ArrivalMarkerTag))
        {
            _sawmill.Error($"Static destination '{proto.ID}' has no ArrivalMarkerTag.");
            return false;
        }

        // ВАЖНО: карта грузится здесь, по требованию, через единый ensure
        if (proto.MapPath is not null)
        {
            var path = proto.MapPath.Value.ToString();
            if (!_maps.EnsureLoadedAndUnpaused(path))
            {
                _sawmill.Error($"Destination '{proto.ID}': failed to ensure map '{path}'.");
                return false;
            }
        }

        if (TryFindMarker(proto.ArrivalMarkerTag, out dest))
            return true;

        _sawmill.Error($"Destination '{proto.ID}': marker '{proto.ArrivalMarkerTag}' not found.");
        return false;
    }

    private bool TryFindMarker(string markerId, out EntityCoordinates dest)
    {
        dest = default;

        var foundAny = false;
        var query = EntityQueryEnumerator<OverworldArrivalMarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var marker, out var xform))
        {
            foundAny = true;
            _sawmill.Info($"Marker seen: id='{marker.MarkerID}' uid={uid} map={xform.MapID} coords={xform.Coordinates}");

            if (!string.Equals(marker.MarkerID, markerId, StringComparison.Ordinal))
                continue;

            dest = xform.Coordinates;
            return true;
        }

        if (!foundAny)
            _sawmill.Warning("No OverworldArrivalMarkerComponent entities found at all.");

        return false;
    }

    private bool TryResolveInstance(TravelDestinationPrototype proto, out EntityCoordinates dest)
    {
        dest = default;
        _sawmill.Warning($"Instance destination '{proto.ID}' not yet implemented.");
        return false;
    }
}
