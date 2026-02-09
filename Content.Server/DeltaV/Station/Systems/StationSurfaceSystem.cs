using Content.Server.Parallax;
using Content.Server.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;

namespace Content.Server.Station.Systems;

public sealed class StationSurfaceSystem : EntitySystem
{
    [Dependency] private readonly BiomeSystem _biome = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationSurfaceComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<StationSurfaceComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.MapPath is not {} path)
            return;

        if (!_mapLoader.TryLoadMap(path, out var loadedMap, out _))
        {
            Log.Error($"Failed to load surface map {ent.Comp.MapPath}!");
            return;
        }

        var map = loadedMap!.Value.Owner;
        var mapId = loadedMap.Value.Comp.MapId;
        _map.SetPaused(map, false);

        // Needs a cherrypick, but this system is unused entirely for now
        //_biome.SetEnabled(map); // generate the terrain after the grids loaded to prevent it getting hidden under it
        ent.Comp.Map = map;
    }
}
