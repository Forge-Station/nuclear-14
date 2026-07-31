using Content.Server._Forge.Warfront.FactionPoints;
using Content.Server._Forge.Warfront.GameRule;
using Content.Server.Popups;
using Content.Shared._Forge.Warfront;
using Content.Shared._Forge.Warfront.CapturePoint;
using Content.Shared._Forge.Warfront.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Warfront.CapturePoint;

public sealed partial class CapturePointSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private FactionPointsSystem _factionPoints = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private WarfrontRuleSystem _warfrontRule = default!;

    private TimeSpan _nextUiRefresh;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CapturePointComponent, CapturePointStartMessage>(OnCaptureStart);
        SubscribeLocalEvent<CapturePointComponent, BoundUIOpenedEvent>(OnUiOpened);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var periodicRefresh = now >= _nextUiRefresh;
        if (periodicRefresh)
            _nextUiRefresh = now + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<CapturePointComponent>();
        while (query.MoveNext(out var uid, out var point))
        {
            var dirty = periodicRefresh;

            if (point.CaptureInProgress && now >= point.CaptureEndTime)
            {
                point.OwnerFaction = point.Attacker;
                point.CaptureInProgress = false;
                point.Attacker = null;
                point.NextPayoutTime = now + TimeSpan.FromMinutes(1);

                if (point.VictoryHoldDuration != null)
                    point.VictoryTime = now + point.VictoryHoldDuration.Value;

                dirty = true;
            }

            if (point.OwnerFaction != null && now >= point.NextPayoutTime)
            {
                _factionPoints.AddPoints(point.OwnerFaction.Value, point.PointsPerMinute);
                point.NextPayoutTime = now + TimeSpan.FromMinutes(1);
                dirty = true;
            }

            if (point.VictoryHoldDuration != null
                && point.OwnerFaction != null
                && point.VictoryTime > TimeSpan.Zero
                && now >= point.VictoryTime)
            {
                DeclareVictory(point.OwnerFaction.Value);
                point.VictoryTime = TimeSpan.Zero;
            }

            if (dirty)
                RefreshUi(uid, point);
        }
    }

    private void OnCaptureStart(EntityUid uid, CapturePointComponent point, CapturePointStartMessage args)
    {
        var actor = args.Actor;
        if (!TryComp<WarfrontFactionComponent>(actor, out var actorFactionComp))
        {
            _popup.PopupEntity(Loc.GetString("capture-point-no-faction"), uid, actor, PopupType.Small);
            return;
        }

        var actorFaction = actorFactionComp.Faction;
        var now = _timing.CurTime;

        if (now < point.CooldownEndTime)
        {
            _popup.PopupEntity(Loc.GetString("capture-point-on-cooldown"), uid, actor, PopupType.Small);
            return;
        }

        if (point.CaptureInProgress)
        {
            _popup.PopupEntity(Loc.GetString("capture-point-already-capturing"), uid, actor, PopupType.Small);
            return;
        }

        if (point.OwnerFaction == actorFaction)
        {
            _popup.PopupEntity(Loc.GetString("capture-point-already-yours"), uid, actor, PopupType.Small);
            return;
        }

        point.CaptureInProgress = true;
        point.Attacker = actorFaction;
        point.CaptureEndTime = now + point.CaptureDuration;
        point.CooldownEndTime = now + point.CaptureCooldown;

        _popup.PopupEntity(Loc.GetString("capture-point-started", ("minutes", (int) point.CaptureDuration.TotalMinutes)), uid, actor, PopupType.Medium);

        RefreshUi(uid, point);
    }

    private void OnUiOpened(EntityUid uid, CapturePointComponent point, BoundUIOpenedEvent args)
    {
        _uiSystem.SetUiState(uid, CapturePointUiKey.Key, BuildState(point));
    }

    private void RefreshUi(EntityUid uid, CapturePointComponent point)
    {
        if (!_uiSystem.IsUiOpen(uid, CapturePointUiKey.Key))
            return;

        _uiSystem.SetUiState(uid, CapturePointUiKey.Key, BuildState(point));
    }

    private CapturePointBoundUserInterfaceState BuildState(CapturePointComponent point)
    {
        return new CapturePointBoundUserInterfaceState
        {
            Owner = point.OwnerFaction,
            CaptureInProgress = point.CaptureInProgress,
            Attacker = point.Attacker,
            CaptureEndTime = point.CaptureEndTime,
            CooldownEndTime = point.CooldownEndTime,
            CaptureDurationSeconds = (int) point.CaptureDuration.TotalSeconds,
            CooldownSeconds = (int) point.CaptureCooldown.TotalSeconds,
            PointsPerMinute = point.PointsPerMinute,
            NextPayoutTime = point.NextPayoutTime,
            Title = point.Title,
        };
    }

    private void DeclareVictory(WarfrontFaction faction)
    {
        _warfrontRule.DeclareVictory(faction);
    }
}
