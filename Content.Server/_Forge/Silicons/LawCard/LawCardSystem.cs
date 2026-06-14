using System.Linq;
using Content.Server.EUI;
using Content.Server.Silicons.Laws;
using Content.Shared._Forge.Silicons.LawCard;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Silicons.Laws;
using Content.Shared.Silicons.Laws.Components;
using Content.Shared.Wires;
using Robust.Shared.Player;

namespace Content.Server._Forge.Silicons.LawCard;

public sealed class LawCardSystem : EntitySystem
{
    [Dependency] private readonly SiliconLawSystem _laws = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EuiManager _euiManager = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly NpcFactionSystem _faction = default!;

    private readonly Dictionary<EntityUid, LawCardEui> _openEditors = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LawCardComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<LawCardComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<LawCardComponent, LawCardDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<LawCardComponent, DroppedEvent>(OnDropped);
        SubscribeLocalEvent<LawCardComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnUseInHand(EntityUid uid, LawCardComponent comp, UseInHandEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.User, out var actor))
            return;

        if (DenyBorgUse(args.User))
        {
            args.Handled = true;
            return;
        }

        OpenEditor(uid, actor.PlayerSession);
        args.Handled = true;
    }

    private void OnAfterInteract(EntityUid uid, LawCardComponent comp, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (!HasComp<SiliconLawProviderComponent>(target))
            return;

        if (DenyBorgUse(args.User))
        {
            args.Handled = true;
            return;
        }

        if (TryComp<WiresPanelComponent>(target, out var panel) && !panel.Open)
        {
            _popup.PopupEntity(Loc.GetString("law-card-panel-closed", ("target", target)), target, args.User);
            args.Handled = true;
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(comp.UploadDelay),
            new LawCardDoAfterEvent(), uid, target: target, used: uid)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true
        });
        args.Handled = true;
    }

    private void OnDoAfter(EntityUid uid, LawCardComponent comp, LawCardDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        if (!HasComp<SiliconLawProviderComponent>(target))
            return;

        if (comp.Laws.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("law-card-empty"), target, args.User);
            return;
        }

        var laws = comp.Laws.Select(l => l.ShallowClone()).ToList();
        _laws.SetLaws(laws, target);
        _popup.PopupEntity(Loc.GetString("law-card-uploaded", ("target", target)), target, args.User);

        // Перепрошивший «застолбляет» борга под СВОЙ набор фракций (вод-теху это даёт боргу
        // [Wastelander, Vault] -> туррели убежища не бьют; рейдеру -> [Raider] и т.д.).
        // Если у прошивающего фракций нет — оставляем дефолт (Wastelander).
        if (args.User != target
            && TryComp<NpcFactionMemberComponent>(args.User, out var userFactions)
            && userFactions.Factions.Count > 0)
        {
            _faction.ClearFactions(target);
            _faction.AddFactions(target, userFactions.Factions);
        }

        args.Handled = true;
    }

    private void OnDropped(EntityUid uid, LawCardComponent comp, DroppedEvent args)
    {
        CloseEditor(uid);
    }

    private void OnTerminating(EntityUid uid, LawCardComponent comp, ref EntityTerminatingEvent args)
    {
        CloseEditor(uid);
    }

    // Борг не умеет пользоваться программатором: со свободной рукой манипулятора он мог бы
    // переписать законы (в т.ч. сам себе). Шлёт попап и возвращает true, если юзер — борг.
    private bool DenyBorgUse(EntityUid user)
    {
        if (!HasComp<BorgChassisComponent>(user))
            return false;

        _popup.PopupEntity(Loc.GetString("law-card-borg-cant-use"), user, user);
        return true;
    }

    private void OpenEditor(EntityUid card, ICommonSession session)
    {
        if (_openEditors.TryGetValue(card, out var existing))
            existing.Close();

        var eui = new LawCardEui(this, card);
        _openEditors[card] = eui;
        _euiManager.OpenEui(eui, session);
        eui.StateDirty();
    }

    private void CloseEditor(EntityUid card)
    {
        if (_openEditors.Remove(card, out var eui))
            eui.Close();
    }

    public void OnEditorClosed(EntityUid card, LawCardEui eui)
    {
        if (_openEditors.TryGetValue(card, out var current) && current == eui)
            _openEditors.Remove(card);
    }

    public List<SiliconLaw> GetLaws(EntityUid card)
    {
        if (!TryComp<LawCardComponent>(card, out var comp))
            return new List<SiliconLaw>();

        return comp.Laws.Select(l => l.ShallowClone()).ToList();
    }

    public void SaveLaws(EntityUid card, List<SiliconLaw> laws, EntityUid user)
    {
        if (!TryComp<LawCardComponent>(card, out var comp))
            return;

        if (!_hands.IsHolding(user, card))
            return;

        // Drop blank laws and cap the count (guards against crafted/autoclicked saves).
        comp.Laws = laws
            .Where(l => !string.IsNullOrWhiteSpace(l.LawString))
            .Take(LawCardComponent.MaxLaws)
            .ToList();
    }
}
