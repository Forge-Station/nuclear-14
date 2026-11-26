using Content.Server.Atmos.EntitySystems;
using Content.Server._Forge.Fire; // Forge-Change: surface fire uses tile extinguishing reactions.
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Server.Chemistry.TileReactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class ExtinguishTileReaction : ITileReaction
    {
        [DataField("coolingTemperature")] private float _coolingTemperature = 2f;

        public FixedPoint2 TileReact(TileRef tile, ReagentPrototype reagent, FixedPoint2 reactVolume)
        {
            if (reactVolume <= FixedPoint2.Zero || tile.Tile.IsEmpty)
                return FixedPoint2.Zero;

            var atmosphereSystem = EntitySystem.Get<AtmosphereSystem>();
            // Forge-Add-Start
            var surfaceFireSystem = EntitySystem.Get<SurfaceFireSystem>();

            // Surface fire is independent from atmospheric hotspots, so always try to extinguish it.
            surfaceFireSystem.ExtinguishAt(tile, reactVolume);
            // Forge-Add-End

            var environment = atmosphereSystem.GetTileMixture(tile.GridUid, null, tile.GridIndices, true);

            if (environment == null || !atmosphereSystem.IsHotspotActive(tile.GridUid, tile.GridIndices))
                return FixedPoint2.Zero;

            environment.Temperature =
                MathF.Max(MathF.Min(environment.Temperature - (_coolingTemperature * 1000f),
                        environment.Temperature / _coolingTemperature), Atmospherics.TCMB);

            atmosphereSystem.ReactTile(tile.GridUid, tile.GridIndices);
            atmosphereSystem.HotspotExtinguish(tile.GridUid, tile.GridIndices);

            return FixedPoint2.Zero;
        }
    }
}
