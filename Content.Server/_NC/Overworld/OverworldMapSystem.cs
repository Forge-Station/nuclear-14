using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;

namespace Content.Server.Overworld;

public sealed class OverworldMapSystem : EntitySystem
{
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    private ISawmill _sawmill = default!;
    private readonly HashSet<string> _loadedPaths = new();

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("overworld.maps");
    }

    public bool EnsureLoadedAndUnpaused(string mapPath)
    {
        var key = mapPath.Replace('\\', '/').Trim().ToLowerInvariant();
        _sawmill.Info($"Ensure map: raw='{mapPath}', key='{key}'");

        if (_loadedPaths.Contains(key))
        {
            _sawmill.Info($"Map cache hit: '{key}'");
            UnpauseAllMaps();
            return true;
        }

        _sawmill.Info($"Map cache miss: '{key}' -> loading");

        if (!_mapLoader.TryLoadGeneric(new Robust.Shared.Utility.ResPath(mapPath), out _, new MapLoadOptions()))
        {
            _sawmill.Error($"Failed to load '{mapPath}'");
            return false;
        }

        _loadedPaths.Add(key);
        UnpauseAllMaps();

        _sawmill.Info($"Map '{key}' loaded and maps unpaused.");
        return true;
    }

    private void UnpauseAllMaps()
    {
        var query = EntityQueryEnumerator<MapComponent>();
        while (query.MoveNext(out _, out var mapComp))
            _mapSystem.SetPaused(mapComp.MapId, false);
    }
}
