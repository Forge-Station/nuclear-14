using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Parallax;
using Content.Shared.Atmos;
using Content.Shared.Interaction;
using Content.Shared.Gravity;
using Content.Shared.Magnits.QuestInstance;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;


namespace Content.Server.Magnits.QuestInstance;


public sealed class QuestInstanceSystem : EntitySystem
{
    // One active instance per board entity.
    private readonly Dictionary<EntityUid, QuestInstance> _activeInstanceByBoard = new();
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly BiomeSystem _biome = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<QuestBoardComponent, EntityTerminatingEvent>(OnBoardTerminating);
        SubscribeLocalEvent<QuestBoardComponent, AfterActivatableUIOpenEvent>(OnBoardUiOpened);
        SubscribeLocalEvent<QuestBoardComponent, QuestBoardSelectDifficultyMessage>(OnBoardSelectDifficulty);
        SubscribeLocalEvent<QuestSignpostComponent, InteractHandEvent>(OnSignpostUsed);
    }

    private static string DifficultyToPresetId(QuestDifficulty difficulty) =>
        difficulty switch
        {
            QuestDifficulty.Easy => "QuestInstanceEasy",
            QuestDifficulty.Medium => "QuestInstanceMedium",
            QuestDifficulty.Hard => "QuestInstanceHard",
            _ => "QuestInstanceEasy"
        };

    public void StartOrJoinInstance(EntityUid boardUid, ICommonSession session, QuestDifficulty difficulty)
    {
        if (_activeInstanceByBoard.TryGetValue(boardUid, out var existing))
        {
            var curTime = _timing.CurTime;
            var alreadyParticipant = existing.Participants.Contains(session.UserId);

            if (curTime >= existing.EndAt)
            {
                _popup.PopupEntity(
                    Loc.GetString("quest-instance-expired"),
                    session.AttachedEntity ?? boardUid,
                    session);
                return;
            }

            if (!alreadyParticipant && curTime > existing.JoinUntil)
            {
                _popup.PopupEntity(
                    Loc.GetString("quest-instance-join-closed", ("seconds", existing.JoinWindowSeconds)),
                    session.AttachedEntity ?? boardUid,
                    session);
                return;
            }

            // Guard: player already on the instance map.
            if (alreadyParticipant && session.AttachedEntity is { } attachedEnt)
            {
                if (Transform(attachedEnt).MapUid == existing.MapUid)
                    return;
            }

            JoinInstance(existing, session);
            SendBoardState(boardUid);
            return;
        }

        var presetId = DifficultyToPresetId(difficulty);
        if (!_proto.TryIndex<QuestInstancePresetPrototype>(presetId, out var preset))
        {
            Log.Error($"QuestInstanceSystem: preset '{presetId}' not found.");
            return;
        }

        var instance = CreateInstance(boardUid, preset);
        if (instance == null)
            return;

        _activeInstanceByBoard[boardUid] = instance;
        JoinInstance(instance, session);
        SendBoardState(boardUid);
    }

    private QuestInstance? CreateInstance(EntityUid boardUid, QuestInstancePresetPrototype preset)
    {
        // Resolve which map file to load, if any.
        var mapPath = preset.MapPaths.Count > 0
            ? _random.Pick(preset.MapPaths)
            : preset.OptionalMapPath;

        // Always create a dedicated map so biome + loaded grid can coexist.
        var mapUid = _mapSystem.CreateMap(out var mapId, false);

        if (preset.BiomeTemplateId != null && !ApplyBiome(mapUid, preset))
        {
            Del(mapUid);
            return null;
        }

        EntityUid gridUid;

        if (mapPath != null)
        {
            // Load salvage grid into this map.
            if (!_mapLoader.TryLoadGrid(mapId, mapPath.Value, out var loadedGrid))
            {
                Log.Error($"QuestInstanceSystem: failed to load grid '{mapPath}'.");
                Del(mapUid);
                return null;
            }

            gridUid = loadedGrid.Value;
        }
        else
        {
            // Biome-only instance.
            if (preset.BiomeTemplateId == null)
            {
                Log.Error(
                    $"QuestInstanceSystem: preset '{preset.ID}' has no MapPaths, OptionalMapPath, or BiomeTemplateId.");
                Del(mapUid);
                return null;
            }

            gridUid = mapUid;
        }

        ForceGridEnvironment(mapUid, gridUid);
        _mapSystem.InitializeMap(mapId);

        var spawnCoords = ComputeSpawnCoordinates(mapUid, gridUid, preset);

        var now = _timing.CurTime;
        var instance = new QuestInstance
        {
            BoardUid = boardUid,
            MapUid = mapUid,
            SpawnCoords = spawnCoords,
            JoinWindowSeconds = preset.JoinWindowSeconds,
            WarningThresholdsSeconds = preset.WarningThresholdsSeconds,
            JoinUntil = now + TimeSpan.FromSeconds(preset.JoinWindowSeconds),
            EndAt = now + TimeSpan.FromSeconds(preset.TimeLimitSeconds)
        };

        var signpost = Spawn(preset.ExitSignpostProto, spawnCoords);
        var signpostComp = EnsureComp<QuestSignpostComponent>(signpost);
        signpostComp.BoardUid = boardUid;

        var barrierRadius = Math.Clamp(
            ComputeBarrierRadius(gridUid, preset),
            1,
            Math.Max(1, preset.MaxBarrierRadius));
        SpawnSquareBarrier(gridUid, preset, barrierRadius);

        return instance;
    }

    private void JoinInstance(QuestInstance instance, ICommonSession session)
    {
        if (session.AttachedEntity is not { } playerEnt)
            return;

        var playerXform = Transform(playerEnt);

        // Save latest return point when joining from outside the instance map.
        if (playerXform.MapUid != instance.MapUid)
            instance.ReturnCoords[session.UserId] = playerXform.Coordinates;

        instance.Participants.Add(session.UserId);

        _transform.SetCoordinates(playerEnt, instance.SpawnCoords);
    }

    private bool ExitPlayer(ICommonSession session, QuestInstance instance)
    {
        if (session.AttachedEntity is not { } playerEnt)
            return true;

        if (instance.ReturnCoords.TryGetValue(session.UserId, out var returnCoords))
        {
            instance.ReturnCoords.Remove(session.UserId);
            _transform.SetCoordinates(playerEnt, returnCoords);
            return true;
        }

        var fallback = GetBoardFallbackCoordinates(instance.BoardUid);
        if (fallback == EntityCoordinates.Invalid)
        {
            Log.Warning($"QuestInstanceSystem: no ReturnCoords and no valid board fallback for {session.UserId}.");
            return false;
        }

        Log.Warning($"QuestInstanceSystem: no ReturnCoords for {session.UserId}, using board fallback.");
        _transform.SetCoordinates(playerEnt, fallback);
        return true;
    }

    private void TryCleanupIfEmpty(EntityUid boardUid, QuestInstance instance)
    {
        if (GetPlayersInInstanceMap(instance).Count > 0)
            return;

        CleanupInstance(boardUid, instance);
    }

    private void ForceCloseInstance(EntityUid boardUid, QuestInstance instance)
    {
        var everyoneEvacuated = true;

        foreach (var session in GetPlayersInInstanceMap(instance))
            everyoneEvacuated &= ExitPlayer(session, instance);

        if (!everyoneEvacuated)
        {
            Log.Warning(
                $"QuestInstanceSystem: aborting cleanup for board {boardUid} because not all players could be evacuated.");
            return;
        }

        CleanupInstance(boardUid, instance);
    }

    private void CleanupInstance(EntityUid boardUid, QuestInstance instance)
    {
        if (!Deleted(instance.MapUid))
            Del(instance.MapUid);

        _activeInstanceByBoard.Remove(boardUid);

        if (!Deleted(boardUid) && !Terminating(boardUid))
            SendBoardState(boardUid);
    }

    public override void Update(float frameTime)
    {
        if (_activeInstanceByBoard.Count == 0)
            return;

        var curTime = _timing.CurTime;

        List<EntityUid>? toClose = null;

        foreach (var (boardUid, instance) in _activeInstanceByBoard)
        {
            var remaining = instance.EndAt - curTime;
            SendWarningPopups(instance, remaining);

            if (remaining <= TimeSpan.Zero)
            {
                toClose ??= new();
                toClose.Add(boardUid);
            }
        }

        if (toClose == null)
            return;

        foreach (var boardUid in toClose)
            if (_activeInstanceByBoard.TryGetValue(boardUid, out var instance))
                ForceCloseInstance(boardUid, instance);
    }

    private bool ApplyBiome(EntityUid mapUid, QuestInstancePresetPrototype preset)
    {
        if (preset.BiomeTemplateId == null)
            return false;

        if (!_proto.TryIndex<BiomeTemplatePrototype>(preset.BiomeTemplateId, out var template))
        {
            Log.Error($"QuestInstanceSystem: biome template '{preset.BiomeTemplateId}' not found.");
            return false;
        }

        _biome.EnsurePlanet(mapUid, template, _random.Next());
        return true;
    }

    private EntityCoordinates ComputeSpawnCoordinates(
        EntityUid mapUid,
        EntityUid gridUid,
        QuestInstancePresetPrototype preset
    )
    {
        if (!TryComp<MapGridComponent>(gridUid, out var gridComp) || gridComp.LocalAABB.IsEmpty())
            return new(mapUid, new(0.5f, 0.5f));

        var aabb = gridComp.LocalAABB;
        var offset = MathF.Max(1f, preset.SpawnDistanceFromGrid);

        // Spawn near the loaded grid, but just outside its right edge.
        var nearGrid = new EntityCoordinates(gridUid, new(aabb.Right + offset, aabb.Center.Y));

        // Prefer map coordinates when map has a biome grid; otherwise keep grid-local coords.
        if (TryComp<MapGridComponent>(mapUid, out _))
        {
            var mapCoords = _transform.ToMapCoordinates(nearGrid);
            return new(mapUid, mapCoords.Position);
        }

        return nearGrid;
    }

    private EntityCoordinates GetBoardFallbackCoordinates(EntityUid boardUid)
    {
        if (Deleted(boardUid))
            return EntityCoordinates.Invalid;

        var boardXform = Transform(boardUid);
        return boardXform.MapUid == null
            ? EntityCoordinates.Invalid
            : boardXform.Coordinates;
    }


    private void ForceGridEnvironment(EntityUid mapUid, EntityUid gridUid)
    {
        var mapGravity = EnsureComp<GravityComponent>(mapUid);
        mapGravity.Enabled = true;
        mapGravity.Inherent = true;
        Dirty(mapUid, mapGravity);

        var gridGravity = EnsureComp<GravityComponent>(gridUid);
        gridGravity.Enabled = true;
        gridGravity.Inherent = true;
        Dirty(gridUid, gridGravity);

        EnsureComp<GridAtmosphereComponent>(gridUid);

        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;

        _atmos.SetMapAtmosphere(mapUid, false, new GasMixture(moles, Atmospherics.T20C));
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

    private void SpawnSquareBarrier(EntityUid gridUid, QuestInstancePresetPrototype preset, int r)
    {
        for (var x = -r; x <= r; x++)
        {
            Spawn(preset.BarrierProto, new(gridUid, new(x + 0.5f, -r + 0.5f)));
            Spawn(preset.BarrierProto, new(gridUid, new(x + 0.5f, r + 0.5f)));
        }

        for (var y = -r + 1; y <= r - 1; y++)
        {
            Spawn(preset.BarrierProto, new(gridUid, new(-r + 0.5f, y + 0.5f)));
            Spawn(preset.BarrierProto, new(gridUid, new(r + 0.5f, y + 0.5f)));
        }
    }

    private List<ICommonSession> GetPlayersInInstanceMap(QuestInstance instance)
    {
        var result = new List<ICommonSession>();

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } ent)
                continue;

            if (Transform(ent).MapUid == instance.MapUid)
                result.Add(session);
        }

        return result;
    }

    private void SendWarningPopups(QuestInstance instance, TimeSpan remaining)
    {
        List<ICommonSession>? playersInMap = null;

        foreach (var threshold in instance.WarningThresholdsSeconds)
        {
            if (remaining.TotalSeconds > threshold)
                continue;

            if (!instance.SentWarnings.Add(threshold))
                continue;

            var msg = Loc.GetString("quest-instance-warning", ("seconds", threshold));
            playersInMap ??= GetPlayersInInstanceMap(instance);

            foreach (var session in playersInMap)
                if (session.AttachedEntity is { } ent)
                    _popup.PopupEntity(msg, ent, session, PopupType.LargeCaution);
        }
    }

    private void OnSignpostUsed(EntityUid uid, QuestSignpostComponent comp, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        if (!_activeInstanceByBoard.TryGetValue(comp.BoardUid, out var instance))
            return;

        args.Handled = true;

        ExitPlayer(actor.PlayerSession, instance);
        TryCleanupIfEmpty(comp.BoardUid, instance);
        SendBoardState(comp.BoardUid);
    }

    private void OnBoardTerminating(EntityUid uid, QuestBoardComponent comp, EntityTerminatingEvent args)
    {
        if (!_activeInstanceByBoard.TryGetValue(uid, out var instance))
            return;

        ForceCloseInstance(uid, instance);
    }

    private void OnBoardUiOpened(EntityUid uid, QuestBoardComponent comp, AfterActivatableUIOpenEvent args) =>
        SendBoardState(uid);

    private void OnBoardSelectDifficulty(
        EntityUid uid,
        QuestBoardComponent comp,
        QuestBoardSelectDifficultyMessage msg
    )
    {
        if (!TryComp<ActorComponent>(msg.Actor, out var actor))
            return;

        StartOrJoinInstance(uid, actor.PlayerSession, msg.Difficulty);
        SendBoardState(uid);
    }

    private void SendBoardState(EntityUid boardUid)
    {
        QuestBoardBoundUserInterfaceState state;

        if (_activeInstanceByBoard.TryGetValue(boardUid, out var instance))
        {
            var remaining = instance.EndAt - _timing.CurTime;
            state = new(
                true,
                Math.Max(0, (int) remaining.TotalSeconds),
                GetPlayersInInstanceMap(instance).Count);
        }
        else
            state = new(false, 0, 0);

        _ui.SetUiState(boardUid, QuestBoardUiKey.Key, state);
    }

    private sealed class QuestInstance
    {
        // All NetUserIds who have ever entered this instance (for rejoin checks).
        public readonly HashSet<NetUserId> Participants = new();

        // Saved world coordinates to return each player to on exit.
        public readonly Dictionary<NetUserId, EntityCoordinates> ReturnCoords = new();

        // Which WarningThresholdsSeconds values have already triggered a popup.
        public readonly HashSet<int> SentWarnings = new();
        public EntityUid BoardUid;
        public TimeSpan EndAt;
        public TimeSpan JoinUntil;
        public int JoinWindowSeconds;
        public EntityUid MapUid;
        public EntityCoordinates SpawnCoords;
        public int[] WarningThresholdsSeconds = new int[0];
    }
}





