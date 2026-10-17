using Content.Shared.ActionBlocker;
using Content.Shared.CCVar;
using Content.Shared.Input;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;

namespace Content.Shared.Standing;

/// <inheritdoc cref="CrawlUnderComponent"/>
public abstract class SharedCrawlUnderSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _speed = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.ToggleCrawlingUnder,
                InputCmdHandler.FromDelegate(HandleCrawlUnderRequest, handle: false))
            .Register<SharedCrawlUnderSystem>();

        SubscribeNetworkEvent<ToggleCrawlUnderEvent>(OnToggleRequest);
        SubscribeLocalEvent<CrawlUnderComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CommandBinds.Unregister<SharedCrawlUnderSystem>();
    }

    private void HandleCrawlUnderRequest(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { } uid
            || !HasComp<CrawlUnderComponent>(uid)
            || !_actionBlocker.CanInteract(uid, null))
            return;

        RaiseNetworkEvent(new ToggleCrawlUnderEvent());
    }

    private void OnToggleRequest(ToggleCrawlUnderEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid
            || !TryComp<CrawlUnderComponent>(uid, out var crawlUnder)
            || !_actionBlocker.CanInteract(uid, null))
            return;

        // Стоящая сущность только сбрасывает флаг: так можно починить глубину отрисовки.
        var newState = !_standing.IsDown(uid) ? false : !crawlUnder.IsCrawlingUnder;

        // Начать ползти под мебелью нельзя, если это выключено в конфиге. Прекратить -
        // можно всегда, на случай если сущность уже под мебелью.
        if (newState && !_config.GetCVar(CCVars.CrawlUnderTables))
        {
            _popups.PopupEntity(Loc.GetString("crawling-under-tables-disabled-popup"), uid, args.SenderSession);
            return;
        }

        SetCrawlingUnder((uid, crawlUnder), newState);
    }

    public void SetCrawlingUnder(Entity<CrawlUnderComponent> ent, bool value)
    {
        if (ent.Comp.IsCrawlingUnder == value)
            return;

        ent.Comp.IsCrawlingUnder = value;
        Dirty(ent);
        _speed.RefreshMovementSpeedModifiers(ent);
    }

    private void OnRefreshMovementSpeed(Entity<CrawlUnderComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!ent.Comp.IsCrawlingUnder || !_standing.IsDown(ent.Owner))
            return;

        args.ModifySpeed(ent.Comp.SpeedModifier, ent.Comp.SpeedModifier);
    }
}
