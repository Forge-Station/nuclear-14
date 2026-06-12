using System.Linq;
using Content.Server.EUI;
using Content.Server.Silicons.Laws;
using Content.Shared._Forge.Silicons;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Silicons.Laws;
using Content.Shared.Silicons.Laws.Components;
using Content.Shared.Wires;
using Robust.Shared.Player;

namespace Content.Server._Forge.Silicons;

/// <summary>
/// Forge-Change: see <see cref="LawCardComponent"/>. Using the card in hand opens the law editor;
/// using it on a silicon (panel open) uploads the card's stored laws.
/// </summary>
public sealed class LawCardSystem : EntitySystem
{
    [Dependency] private readonly SiliconLawSystem _laws = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EuiManager _euiManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LawCardComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<LawCardComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<LawCardComponent, LawCardDoAfterEvent>(OnDoAfter);
    }

    // Using the card in hand (no target) opens the editor.
    private void OnUseInHand(EntityUid uid, LawCardComponent comp, UseInHandEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.User, out var actor))
            return;

        var eui = new LawCardEui(this, uid);
        _euiManager.OpenEui(eui, actor.PlayerSession);
        eui.StateDirty();
        args.Handled = true;
    }

    // Using the card on a silicon uploads the laws (after a do-after).
    private void OnAfterInteract(EntityUid uid, LawCardComponent comp, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (!HasComp<SiliconLawProviderComponent>(target))
            return;

        // Require the maintenance panel open, same gate as accessing the borg's internals.
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

        // Clone so the silicon doesn't share the card's law objects.
        var laws = comp.Laws.Select(l => l.ShallowClone()).ToList();
        _laws.SetLaws(laws, target);
        _popup.PopupEntity(Loc.GetString("law-card-uploaded", ("target", target)), target, args.User);
        args.Handled = true;
    }

    /// <summary>Laws currently written on the card (a clone, safe to hand to the UI).</summary>
    public List<SiliconLaw> GetLaws(EntityUid card)
    {
        if (!TryComp<LawCardComponent>(card, out var comp))
            return new List<SiliconLaw>();

        return comp.Laws.Select(l => l.ShallowClone()).ToList();
    }

    /// <summary>Stores edited laws back onto the card.</summary>
    public void SaveLaws(EntityUid card, List<SiliconLaw> laws)
    {
        if (!TryComp<LawCardComponent>(card, out var comp))
            return;

        comp.Laws = laws;
    }
}
