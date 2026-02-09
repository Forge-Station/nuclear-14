using System.Linq;
using Content.Shared.CCVar;
using Content.Shared.Salvage;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class SalvageTest
{
    /// <summary>
    /// Asserts that all salvage maps have been saved as grids and are loadable.
    /// </summary>
    [Test]
    public async Task AllSalvageMapsLoadableTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        var mapLoader = entManager.System<MapLoaderSystem>();
        var mapManager = server.ResolveDependency<IMapManager>();
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var mapSystem = entManager.System<SharedMapSystem>();
        Assert.That(cfg.GetCVar(CCVars.GridFill), Is.False);

        await server.WaitPost(() =>
        {
            foreach (var salvage in prototypeManager.EnumeratePrototypes<SalvageMapPrototype>())
            {
                var mapFile = salvage.MapPath;

                try
                {
                    Assert.That(mapLoader.TryLoadMap(mapFile, out var loadedMap, out var loadedGrids));
                    Assert.That(loadedGrids, Is.Not.Empty);
                    mapManager.DeleteMap(loadedMap!.Value.Comp.MapId);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to load salvage map {salvage.ID}, was it saved as a map instead of a grid?", ex);
                }
            }
        });
        await server.WaitRunTicks(1);

        await pair.CleanReturnAsync();
    }
}
