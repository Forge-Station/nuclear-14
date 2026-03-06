using System.Numerics;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Gravity;
using Content.Shared.Parallax.Biomes;
using Content.Shared.TimeCycle;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Magnits.QuestInstance;

public sealed partial class QuestInstanceSystem
{
    private QuestInstanceMapEntry? PickMapEntry(QuestInstancePresetPrototype preset)
    {
        if (!preset.HasMapEntries)
            return null;

        var totalWeight = 0f;
        foreach (var entry in preset.MapEntries)
            totalWeight += entry.EffectiveWeight;

        if (totalWeight <= 0f)
        {
            Log.Warning($"QuestInstanceSystem: preset '{preset.ID}' has mapEntries with non-positive total weight. Using uniform pick.");
            return _random.Pick(preset.MapEntries);
        }

        var roll = _random.NextFloat(totalWeight);

        foreach (var entry in preset.MapEntries)
        {
            var weight = entry.EffectiveWeight;
            if (weight <= 0f)
                continue;

            if (roll < weight)
                return entry;

            roll -= weight;
        }

        return preset.MapEntries[^1];
    }

    private bool ApplyBiome(EntityUid mapUid, ProtoId<BiomeTemplatePrototype> biomeTemplateId)
    {
        if (!_proto.TryIndex(biomeTemplateId, out var template))
        {
            Log.Error($"QuestInstanceSystem: biome template '{biomeTemplateId}' not found.");
            return false;
        }

        _biome.EnsurePlanet(mapUid, template, _random.Next());
        return true;
    }

    private void ApplyMapEnvironmentOverrides(EntityUid mapUid, MapId mapId, QuestInstanceMapEntry? mapEntry)
    {
        if (mapEntry == null)
            return;

        if (mapEntry.MapLightColor is { } ambientColor)
        {
            var mapLight = EnsureComp<MapLightComponent>(mapUid);
            mapLight.AmbientLightColor = ambientColor;
            Dirty(mapUid, mapLight);
        }

        if (mapEntry.TimeCyclePaletteId is { } paletteId)
        {
            EnsureComp<MapLightComponent>(mapUid);

            var cycle = EnsureComp<TimeCycleComponent>(mapUid);
            cycle.PalettePrototype = paletteId.ToString();
            cycle.Paused = false;
            Dirty(mapUid, cycle);
        }

        if (mapEntry.WeatherPrototypeId is not { } weatherId)
            return;

        if (!_proto.TryIndex(weatherId, out WeatherPrototype? weather))
        {
            Log.Error($"QuestInstanceSystem: weather prototype '{weatherId}' not found.");
            return;
        }

        _weather.SetWeather(mapId, weather, null);
    }

    private EntityCoordinates ComputeSpawnCoordinates(EntityUid mapUid, EntityUid gridUid, QuestInstancePresetPrototype preset)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var gridComp) || gridComp.LocalAABB.IsEmpty())
            return new(mapUid, new(0.5f, 0.5f));

        var aabb = gridComp.LocalAABB;

        // Biome map: gridUid == mapUid (map entity has a grid component).
        // Spawn just outside the biome grid chunk — the infinite biome has terrain there.
        if (gridUid == mapUid)
        {
            var offset = MathF.Max(1f, preset.SpawnDistanceFromGrid);
            var nearEdge = new EntityCoordinates(gridUid, new(aabb.Right + offset, aabb.Center.Y));
            var mapCoords = _transform.ToMapCoordinates(nearEdge);
            return new(mapUid, mapCoords.Position);
        }

        // Loaded grid (salvage ship etc.): spawn INSIDE the grid at its AABB center so
        // the player lands on actual tiles, not in the void.
        return new EntityCoordinates(gridUid, aabb.Center);
    }

    private static EntityCoordinates OffsetCoordinates(EntityCoordinates origin, Vector2 offset)
    {
        if (origin == EntityCoordinates.Invalid)
            return origin;

        return new(origin.EntityId, origin.Position + offset);
    }

    private EntityCoordinates GetBoardFallbackCoordinates(EntityUid boardUid)
    {
        if (Deleted(boardUid))
            return EntityCoordinates.Invalid;

        var boardXform = Transform(boardUid);
        if (boardXform.MapUid == null)
            return EntityCoordinates.Invalid;

        // Keep fallback spawn next to the board so players do not clip into its fixture.
        return new EntityCoordinates(boardUid, new Vector2(1.25f, 0f));
    }

    private void ForceMapEnvironment(EntityUid mapUid)
    {
        var mapGravity = EnsureComp<GravityComponent>(mapUid);
        mapGravity.Enabled = true;
        mapGravity.Inherent = true;
        Dirty(mapUid, mapGravity);
    }

    private void ForceGridEnvironment(EntityUid gridUid)
    {
        var gridGravity = EnsureComp<GravityComponent>(gridUid);
        gridGravity.Enabled = true;
        gridGravity.Inherent = true;
        Dirty(gridUid, gridGravity);

        // Remove any stale GridAtmosphereComponent that may have been loaded from the
        // grid file (possibly with vacuum tile data), then add a fresh one so it
        // initialises from the map atmosphere set in ApplyBreathableMapAtmosphere.
        RemComp<GridAtmosphereComponent>(gridUid);
        EnsureComp<GridAtmosphereComponent>(gridUid);
    }

    private void ApplyBreathableMapAtmosphere(EntityUid mapUid, float temperatureCelsius)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;

        var temperatureKelvin = MathF.Max(Atmospherics.TCMB, Atmospherics.T0C + temperatureCelsius);
        _atmos.SetMapAtmosphere(mapUid, false, new GasMixture(moles, temperatureKelvin));
    }

    private int ComputeBarrierRadius(EntityUid gridUid, QuestInstancePresetPrototype preset)
    {
        var padding = Math.Max(0, preset.BarrierPadding);

        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return 1 + padding;

        var aabb = grid.LocalAABB;
        var tileSize = (float) grid.TileSize;

        if (aabb.Width <= tileSize && aabb.Height <= tileSize)
            return 1 + padding;

        var minX = (int) MathF.Floor(aabb.Left / tileSize);
        var maxX = (int) MathF.Ceiling(aabb.Right / tileSize) - 1;
        var minY = (int) MathF.Floor(aabb.Bottom / tileSize);
        var maxY = (int) MathF.Ceiling(aabb.Top / tileSize) - 1;

        var baseRadius = Math.Max(
            Math.Max(Math.Abs(minX), Math.Abs(maxX)),
            Math.Max(Math.Abs(minY), Math.Abs(maxY)));

        if (baseRadius < 1)
            baseRadius = 1;

        return baseRadius + padding;
    }

    private void QueueSquareBarrier(QuestBoardComponent board, EntityUid gridUid, QuestInstancePresetPrototype preset, int r)
    {
        board.BarrierProto = preset.BarrierProto;

        for (var x = -r; x <= r; x++)
        {
            board.PendingBarrierCoords.Add(new EntityCoordinates(gridUid, new Vector2(x + 0.5f, -r + 0.5f)));
            board.PendingBarrierCoords.Add(new EntityCoordinates(gridUid, new Vector2(x + 0.5f, r + 0.5f)));
        }

        for (var y = -r + 1; y <= r - 1; y++)
        {
            board.PendingBarrierCoords.Add(new EntityCoordinates(gridUid, new Vector2(-r + 0.5f, y + 0.5f)));
            board.PendingBarrierCoords.Add(new EntityCoordinates(gridUid, new Vector2(r + 0.5f, y + 0.5f)));
        }
    }

    private void ProcessPendingBarrier(QuestBoardComponent board, int spawnBudget = 16)
    {
        for (var i = 0; i < spawnBudget && board.PendingBarrierCoords.Count > 0; i++)
        {
            var idx = board.PendingBarrierCoords.Count - 1;
            var coords = board.PendingBarrierCoords[idx];
            board.PendingBarrierCoords.RemoveAt(idx);
            Spawn(board.BarrierProto, coords);
        }
    }
}
