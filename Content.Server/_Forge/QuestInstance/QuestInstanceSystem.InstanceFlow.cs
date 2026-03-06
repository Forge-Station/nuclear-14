using System.Linq;
using Content.Shared._Forge.QuestInstance;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;


namespace Content.Server._Forge.QuestInstance;


public sealed partial class QuestInstanceSystem
{
    public void StartOrJoinInstance(EntityUid boardUid, ICommonSession session, QuestDifficulty difficulty)
    {
        if (!TryComp<QuestBoardComponent>(boardUid, out var board))
            return;

        if (board.HasActiveInstance)
        {
            var curTime = _timing.CurTime;
            var alreadyParticipant = board.Participants.Contains(session.UserId);

            if (curTime >= board.EndAt)
            {
                _popup.PopupEntity(
                    Robust.Shared.Localization.Loc.GetString("quest-instance-expired"),
                    session.AttachedEntity ?? boardUid,
                    session);
                return;
            }

            if (!alreadyParticipant && curTime > board.JoinUntil)
            {
                _popup.PopupEntity(
                    Robust.Shared.Localization.Loc.GetString(
                        "quest-instance-join-closed",
                        ("seconds", board.JoinWindowSeconds)),
                    session.AttachedEntity ?? boardUid,
                    session);
                return;
            }

            // Guard: player already on the instance map.
            if (alreadyParticipant && session.AttachedEntity is { } attachedEnt)
            {
                if (Transform(attachedEnt).MapUid == board.MapUid)
                    return;
            }

            JoinInstance(board, session);
            SendBoardState(boardUid, board);
            return;
        }

        var presetId = DifficultyToPresetId(difficulty);
        if (!_proto.TryIndex<QuestInstancePresetPrototype>(presetId, out var preset))
        {
            Log.Error($"QuestInstanceSystem: preset '{presetId}' not found.");
            return;
        }

        if (!CreateInstance(boardUid, board, preset))
            return;

        JoinInstance(board, session);
        SendBoardState(boardUid, board);
    }

    private bool CreateInstance(EntityUid boardUid, QuestBoardComponent board, QuestInstancePresetPrototype preset)
    {
        ClearInstanceState(board);

        var selectedMapEntry = PickMapEntry(preset);

        ResPath? mapPath;
        if (selectedMapEntry != null)
            mapPath = selectedMapEntry.MapPath;
        else if (preset.MapPaths.Count > 0)
            mapPath = _random.Pick(preset.MapPaths);
        else
            mapPath = preset.OptionalMapPath;

        var biomeTemplateId = selectedMapEntry?.BiomeTemplateId ?? preset.BiomeTemplateId;
        var atmosphereTemperatureCelsius =
            selectedMapEntry?.AtmosphereTemperatureCelsius ?? preset.AtmosphereTemperatureCelsius;

        // Always create a dedicated map so biome + loaded grid can coexist.
        var mapUid = _mapSystem.CreateMap(out var mapId, false);

        // Atmosphere and gravity are enabled immediately to avoid vacuum spikes while content is spawning.
        ForceMapEnvironment(mapUid);
        ApplyBreathableMapAtmosphere(mapUid, atmosphereTemperatureCelsius);

        if (biomeTemplateId is { } biomeId && !ApplyBiome(mapUid, biomeId))
        {
            _mapSystem.DeleteMap(mapId);
            return false;
        }

        EntityUid gridUid;

        if (mapPath != null)
        {
            // Load salvage grid into this map.
            if (!_mapLoader.TryLoadGrid(mapId, mapPath.Value, out var loadedGrid))
            {
                Log.Error($"QuestInstanceSystem: failed to load grid '{mapPath}'.");
                _mapSystem.DeleteMap(mapId);
                return false;
            }

            gridUid = loadedGrid.Value;
        }
        else
        {
            // Biome-only instance.
            if (biomeTemplateId == null)
            {
                Log.Error(
                    $"QuestInstanceSystem: preset '{preset.ID}' has no MapEntries, MapPaths, OptionalMapPath, or BiomeTemplateId.");
                _mapSystem.DeleteMap(mapId);
                return false;
            }

            gridUid = mapUid;
        }

        ForceGridEnvironment(gridUid);
        ForceGridBreathableAtmosphere(gridUid, atmosphereTemperatureCelsius);

        _mapSystem.InitializeMap(mapId);

        // Re-apply atmosphere AFTER map init so map-atmos tiles keep the selected profile,
        // then normalize loaded grid tile mixtures to prevent decompression spikes.
        ApplyBreathableMapAtmosphere(mapUid, atmosphereTemperatureCelsius);
        ForceGridBreathableAtmosphere(gridUid, atmosphereTemperatureCelsius);

        var signpostCoords = ComputeSpawnCoordinates(mapUid, gridUid, preset);
        var playerSpawnCoords = OffsetCoordinates(signpostCoords, new(1.25f, 0f));

        var now = _timing.CurTime;
        board.HasActiveInstance = true;
        board.MapUid = mapUid;
        board.SpawnCoords = playerSpawnCoords;
        board.JoinWindowSeconds = preset.JoinWindowSeconds;
        board.WarningThresholdsSeconds = preset.WarningThresholdsSeconds.ToArray();
        board.JoinUntil = now + TimeSpan.FromSeconds(preset.JoinWindowSeconds);
        board.EndAt = now + TimeSpan.FromSeconds(preset.TimeLimitSeconds);

        var signpost = Spawn(preset.ExitSignpostProto, signpostCoords);
        var signpostComp = EnsureComp<QuestSignpostComponent>(signpost);
        signpostComp.BoardUid = boardUid;

        var barrierRadius = Math.Clamp(
            ComputeBarrierRadius(gridUid, preset),
            1,
            Math.Max(1, preset.MaxBarrierRadius));
        QueueSquareBarrier(board, gridUid, preset, barrierRadius);
        ProcessPendingBarrier(board);

        ApplyMapEnvironmentOverrides(mapUid, mapId, selectedMapEntry);
        return true;
    }

    private void JoinInstance(QuestBoardComponent board, ICommonSession session)
    {
        if (session.AttachedEntity is not { } playerEnt)
            return;

        var playerXform = Transform(playerEnt);

        // Save latest return point when joining from outside the instance map.
        if (playerXform.MapUid != board.MapUid)
            board.ReturnCoords[playerEnt] = playerXform.Coordinates;

        board.Participants.Add(session.UserId);
        board.PresentPlayers.Add(playerEnt);
        TeleportWithPulledEntity(playerEnt, board.SpawnCoords);
    }

    private void ExitPlayer(EntityUid boardUid, ICommonSession session, QuestBoardComponent board)
    {
        if (session.AttachedEntity is not { } playerEnt)
            return;

        ExitEntity(playerEnt, boardUid, board, session.UserId.ToString());
    }

    private bool ExitEntity(EntityUid playerEnt, EntityUid boardUid, QuestBoardComponent board, string debugLabel)
    {
        if (board.ReturnCoords.Remove(playerEnt, out var returnCoords))
        {
            TeleportWithPulledEntity(playerEnt, returnCoords);
            board.PresentPlayers.Remove(playerEnt);
            return true;
        }

        var fallback = GetBoardFallbackCoordinates(boardUid);
        if (fallback == EntityCoordinates.Invalid)
        {
            Log.Warning($"QuestInstanceSystem: no ReturnCoords and no valid board fallback for {debugLabel}.");
            return false;
        }

        Log.Warning($"QuestInstanceSystem: no ReturnCoords for {debugLabel}, using board fallback.");
        TeleportWithPulledEntity(playerEnt, fallback);
        board.PresentPlayers.Remove(playerEnt);
        return true;
    }

    private void PruneReturnCoords(QuestBoardComponent board)
    {
        List<EntityUid>? stale = null;

        foreach (var (uid, _) in board.ReturnCoords)
            if (Deleted(uid) || Transform(uid).MapUid != board.MapUid)
            {
                stale ??= new();
                stale.Add(uid);
            }

        if (stale == null)
            return;

        foreach (var uid in stale)
            board.ReturnCoords.Remove(uid);
    }

    private void PrunePresentPlayers(QuestBoardComponent board)
    {
        List<EntityUid>? stale = null;

        foreach (var uid in board.PresentPlayers)
            if (Deleted(uid) || Transform(uid).MapUid != board.MapUid)
            {
                stale ??= new();
                stale.Add(uid);
            }

        if (stale == null)
            return;

        foreach (var uid in stale)
            board.PresentPlayers.Remove(uid);
    }

    private void TryCleanupIfEmpty(EntityUid boardUid, QuestBoardComponent board)
    {
        PrunePresentPlayers(board);
        if (board.PresentPlayers.Count > 0)
            return;

        PruneReturnCoords(board);
        if (board.ReturnCoords.Count > 0)
            return;

        CleanupInstance(boardUid, board);
    }

    private void ForceCloseInstance(EntityUid boardUid, QuestBoardComponent board)
    {
        var everyoneEvacuated = true;

        PrunePresentPlayers(board);
        PruneReturnCoords(board);

        var toEvacuate = new HashSet<EntityUid>(board.ReturnCoords.Keys);
        toEvacuate.UnionWith(board.PresentPlayers);

        foreach (var uid in toEvacuate)
            everyoneEvacuated &= ExitEntity(uid, boardUid, board, uid.ToString());

        if (!everyoneEvacuated)
        {
            Log.Warning(
                $"QuestInstanceSystem: aborting cleanup for board {boardUid} because not all players could be evacuated.");
            return;
        }

        CleanupInstance(boardUid, board);
    }

    private void CleanupInstance(EntityUid boardUid, QuestBoardComponent board)
    {
        if (!Deleted(board.MapUid))
        {
            var mapId = Transform(board.MapUid).MapID;
            if (mapId != MapId.Nullspace)
            {
                if (TryComp<WeatherComponent>(board.MapUid, out var weatherComp))
                {
                    weatherComp.Weather.Clear();
                    Dirty(board.MapUid, weatherComp);
                }

                _mapSystem.DeleteMap(mapId);
            }
        }

        ClearInstanceState(board);

        if (!Deleted(boardUid) && !Terminating(boardUid))
            SendBoardState(boardUid, board);
    }

    private static void ClearInstanceState(QuestBoardComponent board)
    {
        board.HasActiveInstance = false;
        board.Participants.Clear();
        board.PresentPlayers.Clear();
        board.ReturnCoords.Clear();
        board.SentWarnings.Clear();
        board.PendingBarrierCoords.Clear();

        board.BarrierProto = "QuestInvisibleWall";
        board.EndAt = TimeSpan.Zero;
        board.JoinUntil = TimeSpan.Zero;
        board.JoinWindowSeconds = 0;
        board.MapUid = EntityUid.Invalid;
        board.SpawnCoords = EntityCoordinates.Invalid;
        board.WarningThresholdsSeconds = Array.Empty<int>();
    }

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;
        List<EntityUid>? toClose = null;

        var query = EntityQueryEnumerator<QuestBoardComponent>();
        while (query.MoveNext(out var boardUid, out var board))
        {
            if (!board.HasActiveInstance)
                continue;

            ProcessPendingBarrier(board);

            var remaining = board.EndAt - curTime;
            SendWarningPopups(board, remaining);

            if (remaining <= TimeSpan.Zero)
            {
                toClose ??= new();
                toClose.Add(boardUid);
            }
        }

        if (toClose == null)
            return;

        foreach (var boardUid in toClose)
        {
            if (!TryComp<QuestBoardComponent>(boardUid, out var board) || !board.HasActiveInstance)
                continue;

            ForceCloseInstance(boardUid, board);
        }
    }
}
