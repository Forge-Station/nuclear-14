using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Components;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server.Maps;

/// <summary>
/// Loads every map and resaves it into the data folder.
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed class ResaveCommand : LocalizedCommands
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IResourceManager _res = default!;

    public override string Command => "resave";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var loader = _entManager.System<MapLoaderSystem>();
        var mapSys = _entManager.System<SharedMapSystem>();

        foreach (var fn in _res.ContentFindFiles(new ResPath("/Maps/")))
        {
            var mapUid = mapSys.CreateMap(out var mapId, runMapInit: false);
            var options = new MapLoadOptions
            {
                MergeMap = mapId,
                DeserializationOptions = new DeserializationOptions { StoreYamlUids = true },
            };
            loader.TryLoadGeneric(fn, out _, options);

            // Process deferred component removals.
            _entManager.CullRemovedComponents();

            var mapXform = _entManager.GetComponent<TransformComponent>(mapUid);

            if (_entManager.HasComponent<LoadedMapComponent>(mapUid) || mapXform.ChildCount != 1)
            {
                loader.TrySaveMap(mapId, fn);
            }
            else if (mapXform.ChildEnumerator.MoveNext(out var child))
            {
                loader.TrySaveGrid(child, fn);
            }

            _mapManager.DeleteMap(mapId);
        }
    }
}
