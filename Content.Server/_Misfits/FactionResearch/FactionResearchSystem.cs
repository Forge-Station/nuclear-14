using System.Linq;
using Content.Server.Cargo.Components;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Shared._Misfits.FactionResearch;
using Content.Shared._Misfits.FactionResearch.Components;
using Content.Shared._Misfits.FactionResearch.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.FactionResearch;

public sealed class FactionResearchSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedJobSystem _jobs = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly RandomResearchSheetSystem _sheet = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    private static readonly SoundPathSpecifier FaxSound = new("/Audio/Machines/printer.ogg");
    private readonly Dictionary<EntityUid, EntityUid> _tabsUsers = new();
    private readonly Dictionary<EntityUid, ICommonSession> _tabsSessions = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactionResearchComponent, AfterActivatableUIOpenEvent>(OnAfterUiOpen);
        SubscribeLocalEvent<FactionResearchComponent, FactionResearchPrintMessage>(OnPrint);
        SubscribeLocalEvent<FactionResearchComponent, FactionResearchConvertMessage>(OnConvert);
        SubscribeLocalEvent<FactionResearchComponent, FactionResearchWithdrawDiskMessage>(OnWithdrawDisk);
        SubscribeLocalEvent<FactionResearchComponent, BoundUIClosedEvent>(OnBoundUiClosed);
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FactionPointSourceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var source, out var xform))
        {
            if (now < source.NextUpdateTime)
                continue;

            source.NextUpdateTime = now + TimeSpan.FromSeconds(1);

            if (!source.Active)
                continue;

            if (!TryComp<ApcPowerReceiverComponent>(uid, out var power) || !power.Powered)
                continue;

            if (!TryFindBench(xform, source.Radius, out var bench))
                continue;

            if (!TryComp<FactionResearchComponent>(bench, out var research))
                continue;

            research.Points[source.Faction] = GetPoints(research, source.Faction) + source.PointsPerSecond;
            Dirty(bench, research);
        }
    }

    private bool TryFindBench(TransformComponent sourceXform, float radius, out EntityUid bench)
    {
        bench = default;

        var sourcePos = sourceXform.WorldPosition;
        var query = EntityQueryEnumerator<FactionResearchComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != sourceXform.MapID)
                continue;

            if ((xform.WorldPosition - sourcePos).Length() > radius)
                continue;

            bench = uid;
            return true;
        }

        return false;
    }

    private int GetPoints(FactionResearchComponent research, ProtoId<DepartmentPrototype> faction)
    {
        return research.Points.GetValueOrDefault(faction, 0);
    }

    private ProtoId<DepartmentPrototype>? GetUserFaction(EntityUid user)
    {
        if (!TryComp<MindContainerComponent>(user, out var mindContainer))
            return null;

        if (!_jobs.MindTryGetJob(mindContainer.Mind, out _, out var jobPrototype))
            return null;

        if (!_jobs.TryGetDepartment(jobPrototype.ID, out var department))
            return null;

        return department.ID;
    }

    private void OnAfterUiOpen(EntityUid uid, FactionResearchComponent component, AfterActivatableUIOpenEvent args)
    {
        _tabsUsers[uid] = args.User;

        if (_player.TryGetSessionByEntity(args.User, out var session))
            _tabsSessions[uid] = session;

        UpdateUi(uid, component, args.User);
    }

    private void OnBoundUiClosed(EntityUid uid, FactionResearchComponent component, BoundUIClosedEvent args)
    {
        if (args.UiKey is not FactionResearchUiKey)
            return;

        _tabsUsers.Remove(uid);
        _tabsSessions.Remove(uid);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (args.Player.AttachedEntity is not { } body)
            return;

        foreach (var (uid, session) in _tabsSessions)
        {
            if (session != args.Player)
                continue;

            _tabsUsers[uid] = body;

            if (TryComp<FactionResearchComponent>(uid, out var research))
                UpdateUi(uid, research, body);
        }
    }

    private void OnPrint(EntityUid uid, FactionResearchComponent component, FactionResearchPrintMessage args)
    {
        var faction = GetUserFaction(args.Actor);
        if (faction == null)
            return;

        if (!_proto.TryIndex(args.PrintId, out FactionResearchPrintPrototype? print))
            return;

        if (print.Faction != faction.Value)
            return;

        var points = GetPoints(component, faction.Value);
        if (points < print.Cost)
            return;

        component.Points[faction.Value] = points - print.Cost;

        if (print.RandomSheet is { } sheetConfig)
        {
            var count = _sheet.Count(sheetConfig);
            if (count > 0)
            {
                Spawn(_sheet.GetProto(sheetConfig, _random.Next(count)), Transform(uid).Coordinates);
                _audio.PlayPvs(FaxSound, uid);
            }
        }
        else if (print.Item is { } item)
        {
            Spawn(item, Transform(uid).Coordinates);
        }

        Dirty(uid, component);
        UpdateUi(uid, component, args.Actor);
    }

    private void OnConvert(EntityUid uid, FactionResearchComponent component, FactionResearchConvertMessage args)
    {
        var faction = GetUserFaction(args.Actor);
        if (faction == null)
            return;

        if (!TryComp<StorageComponent>(uid, out var storage))
            return;

        if (!TryGetEntity(args.Item, out var item) || item == null)
            return;

        if (!storage.Container.Contains(item.Value))
            return;

        var points = GetItemValue(item.Value);
        if (points <= 0)
            return;

        component.Points[faction.Value] = GetPoints(component, faction.Value) + points;
        _popup.PopupEntity(Loc.GetString("faction-research-converted", ("points", points)), uid, args.Actor);
        QueueDel(item.Value);
        Dirty(uid, component);
        UpdateUi(uid, component, args.Actor);
    }

    private void OnWithdrawDisk(EntityUid uid, FactionResearchComponent component, FactionResearchWithdrawDiskMessage args)
    {
        var faction = GetUserFaction(args.Actor);
        if (faction == null || args.Amount <= 0)
            return;

        var points = GetPoints(component, faction.Value);
        if (points < args.Amount)
            return;

        component.Points[faction.Value] = points - args.Amount;

        var disk = Spawn("N14FactionResearchDiskBase", Transform(uid).Coordinates);
        var value = EnsureComp<FactionResearchValueComponent>(disk);
        value.PointsOverride = args.Amount;
        Dirty(disk, value);

        Dirty(uid, component);
        UpdateUi(uid, component, args.Actor);
    }

    public int GetItemValue(EntityUid item)
    {
        if (TryComp<FactionResearchValueComponent>(item, out var value) && value.PointsOverride is { } over)
            return over;

        var multiplier = 1f;
        if (value?.Category is { } category && _proto.TryIndex(category, out FactionResearchValuePrototype? valueProto))
            multiplier = valueProto.Multiplier;

        var price = 0.0;
        if (TryComp<StaticPriceComponent>(item, out var staticPrice))
            price = staticPrice.Price;

        return (int) MathF.Ceiling((float) price * multiplier);
    }

    public void UpdateUi(EntityUid uid, FactionResearchComponent? component = null, EntityUid? user = null)
    {
        if (!Resolve(uid, ref component, false) || user == null)
            return;

        _ui.SetUiState(uid, FactionResearchUiKey.Key, BuildResearchState(uid, component, user.Value));
    }

    private FactionResearchBoundInterfaceState BuildResearchState(EntityUid uid, FactionResearchComponent component, EntityUid user)
    {
        var faction = GetUserFaction(user);

        var state = new FactionResearchBoundInterfaceState
        {
            Faction = faction?.Id ?? string.Empty,
            Points = faction == null ? 0 : GetPoints(component, faction.Value),
            CanCraft = false,
        };

        if (faction != null)
        {
            foreach (var print in _proto.EnumeratePrototypes<FactionResearchPrintPrototype>())
            {
                if (print.Faction != faction.Value)
                    continue;

                state.Prints.Add(new FactionResearchPrintState
                {
                    Id = print.ID,
                    Name = print.Name is { } locName
                        ? Loc.GetString(locName)
                        : print.Item is { } item ? _proto.Index<EntityPrototype>(item).Name : print.ID,
                    Category = print.Category is { } category ? Loc.GetString(category) : string.Empty,
                    Tier = print.Tier,
                    Cost = print.Cost,
                    Available = state.Points >= print.Cost,
                });
            }
        }

        if (TryComp<StorageComponent>(uid, out var storage))
        {
            foreach (var item in storage.Container.ContainedEntities)
            {
                var value = GetItemValue(item);
                if (value <= 0)
                    continue;

                state.Convertibles.Add(new FactionResearchConvertState
                {
                    Item = GetNetEntity(item),
                    Name = MetaData(item).EntityName,
                    Points = value,
                });
            }
        }

        return state;
    }
}
