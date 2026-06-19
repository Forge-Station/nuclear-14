using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.EUI;
using Content.Server.Silicons.Laws;
using Content.Shared._Forge.Silicons.LawCard;
using Content.Shared.Access.Systems;
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
    [Dependency] private readonly AccessSystem _access = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;

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

    private void OnUseInHand(Entity<LawCardComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !TryComp<ActorComponent>(args.User, out var actor))
            return;

        if (DenyBorgUse(args.User))
        {
            args.Handled = true;
            return;
        }

        OpenEditor(ent.Owner, actor.PlayerSession);
        args.Handled = true;
    }

    private void OnAfterInteract(Entity<LawCardComponent> ent, ref AfterInteractEvent args)
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

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.UploadDelay),
            new LawCardDoAfterEvent(), ent.Owner, target: target, used: ent.Owner)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true
        });
        args.Handled = true;
    }

    private void OnDoAfter(Entity<LawCardComponent> ent, ref LawCardDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        if (!HasComp<SiliconLawProviderComponent>(target))
            return;

        if (ent.Comp.Laws.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("law-card-empty"), target, args.User);
            return;
        }

        _laws.SetLaws(CloneLaws(ent.Comp), target);
        _popup.PopupEntity(Loc.GetString("law-card-uploaded", ("target", target)), target, args.User);

        // если включено, борг заодно перенимает фракции и допуски того, кто его прошил. если нет — меняем только законы.
        if (ent.Comp.OverwriteAccess && args.User != target)
            AdoptOwnership(target, args.User);

        args.Handled = true;
    }

    // борг берёт фракции и допуски прошившего — те, что у того сейчас (ID-карта, инвентарь). обоим показываем «Допуск изменён».
    private void AdoptOwnership(EntityUid target, EntityUid user)
    {
        if (TryComp<NpcFactionMemberComponent>(user, out var userFactions) && userFactions.Factions.Count > 0)
        {
            _faction.ClearFactions(target);
            _faction.AddFactions(target, userFactions.Factions);
        }

        _access.TrySetTags(target, _accessReader.FindAccessTags(user));

        _popup.PopupEntity(Loc.GetString("law-card-access-changed"), target, user);
        _popup.PopupEntity(Loc.GetString("law-card-access-changed"), target, target);
    }

    private void OnDropped(Entity<LawCardComponent> ent, ref DroppedEvent args)
    {
        CloseEditor(ent.Owner);
    }

    private void OnTerminating(Entity<LawCardComponent> ent, ref EntityTerminatingEvent args)
    {
        CloseEditor(ent.Owner);
    }

    // сам борг карту использовать не может — иначе свободной рукой-манипулятором переписал бы законы, в том числе себе.
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

        return CloneLaws(comp);
    }

    // Отдаём копии законов, а не сам список карты — чтобы правки на стороне борга/окна не меняли карту.
    private static List<SiliconLaw> CloneLaws(LawCardComponent comp)
    {
        return comp.Laws.Select(l => l.ShallowClone()).ToList();
    }

    public bool GetOverwriteAccess(EntityUid card)
    {
        return TryComp<LawCardComponent>(card, out var comp) && comp.OverwriteAccess;
    }

    public void SaveLaws(EntityUid card, List<SiliconLaw> laws, bool overwriteAccess, EntityUid user)
    {
        if (!TryComp<LawCardComponent>(card, out var comp))
            return;

        if (!_hands.IsHolding(user, card))
            return;

        // выкидываем пустые законы и режем список по максимуму — на случай кривых или накликанных сохранений.
        comp.Laws = laws
            .Where(l => !string.IsNullOrWhiteSpace(l.LawString))
            .Take(LawCardComponent.MaxLaws)
            .ToList();

        comp.OverwriteAccess = overwriteAccess;
    }
}
