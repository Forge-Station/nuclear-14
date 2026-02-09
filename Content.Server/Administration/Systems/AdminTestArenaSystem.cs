using System.Linq;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Systems;

/// <summary>
/// This handles the administrative test arena maps, and loading them.
/// </summary>
public sealed class AdminTestArenaSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly MapLoaderSystem _map = default!;
    [Dependency] private readonly MetaDataSystem _metaDataSystem = default!;

    public const string ArenaMapPath = "/Maps/Test/admin_test_arena.yml";

    public Dictionary<NetUserId, EntityUid> ArenaMap { get; private set; } = new();
    public Dictionary<NetUserId, EntityUid?> ArenaGrid { get; private set; } = new();

    public (EntityUid Map, EntityUid? Grid) AssertArenaLoaded(ICommonSession admin)
    {
        if (ArenaMap.TryGetValue(admin.UserId, out var arenaMap) && !Deleted(arenaMap) && !Terminating(arenaMap))
        {
            if (ArenaGrid.TryGetValue(admin.UserId, out var arenaGrid) && !Deleted(arenaGrid) && !Terminating(arenaGrid.Value))
            {
                return (arenaMap, arenaGrid);
            }
            else
            {
                ArenaGrid[admin.UserId] = null;
                return (arenaMap, null);
            }
        }

        if (!_map.TryLoadMap(new ResPath(ArenaMapPath), out var loadedMap, out var grids) || grids == null || grids.Count == 0)
        {
            // Fallback: create empty map
            ArenaMap[admin.UserId] = _mapManager.GetMapEntityId(_mapManager.CreateMap());
            _metaDataSystem.SetEntityName(ArenaMap[admin.UserId], $"ATAM-{admin.Name}");
            ArenaGrid[admin.UserId] = null;
            return (ArenaMap[admin.UserId], ArenaGrid[admin.UserId]);
        }

        ArenaMap[admin.UserId] = loadedMap!.Value.Owner;
        _metaDataSystem.SetEntityName(ArenaMap[admin.UserId], $"ATAM-{admin.Name}");
        if (grids.Count != 0)
        {
            var firstGrid = grids.First().Owner;
            _metaDataSystem.SetEntityName(firstGrid, $"ATAG-{admin.Name}");
            ArenaGrid[admin.UserId] = firstGrid;
        }
        else
        {
            ArenaGrid[admin.UserId] = null;
        }

        return (ArenaMap[admin.UserId], ArenaGrid[admin.UserId]);
    }
}
