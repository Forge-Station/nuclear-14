using Content.Server.DoAfter;
using Content.Shared.DoAfter;
using Content.Shared._Forge.Weapons.Ranged.Components;
using Content.Shared._Forge.Weapons.Ranged.Systems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Random;


namespace Content.Server._Forge.Weapons.Ranged.Systems;


public sealed class GunConditionSystem : SharedGunConditionSystem
{
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunComponent, ComponentStartup>(OnGunStartup);
        SubscribeLocalEvent<GunConditionRepairToolComponent, ComponentStartup>(OnRepairToolStartup);
        SubscribeLocalEvent<GunConditionComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<GunConditionComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<GunConditionComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<GunConditionComponent, GunConditionRepairDoAfterEvent>(OnRepairFinished);
        SubscribeLocalEvent<GunConditionComponent, GunConditionUnjamDoAfterEvent>(OnUnjamFinished);
    }

    private void OnGunStartup(EntityUid uid, GunComponent component, ref ComponentStartup args)
    {
        if (!HasComp<ItemComponent>(uid))
            return;

        EnsureComp<GunConditionComponent>(uid);
    }

    private void OnRepairToolStartup(Entity<GunConditionRepairToolComponent> ent, ref ComponentStartup args)
    {
        // Защита от бессмысленной конфигурации Uses = 0.
        // Такой инструмент не должен существовать, поэтому поднимаем до 1.
        if (ent.Comp.Uses != 0)
            return;

        Log.Warning($"Gun condition repair tool {ent.Owner} has Uses=0 in prototype. Forcing Uses=1.");
        ent.Comp.Uses = 1;
    }

    private void OnGunShot(Entity<GunConditionComponent> ent, ref GunShotEvent args)
    {
        if (IsBroken(ent.Comp))
            return;

        // 1) Применяем износ от текущей серии выстрелов.
        var shotsFired = Math.Max(args.Ammo.Count, 1);
        var wearAmount = ent.Comp.WearPerShot * shotsFired;
        var nextCondition = Math.Max(ent.Comp.BrokenThreshold, ent.Comp.Condition - wearAmount);
        ent.Comp.Condition = nextCondition;

        // 2) Если дошли до порога поломки, гарантированно клиним и выходим.
        if (IsBroken(ent.Comp))
        {
            ent.Comp.Jammed = true;
            Dirty(ent);
            return;
        }

        // 3) Иначе роллим клин от уже обновлённой прочности.
        if (!ent.Comp.Jammed && _random.Prob(GetJamChance(ent.Comp)))
            ent.Comp.Jammed = true;

        Dirty(ent);
    }

    private void OnInteractHand(Entity<GunConditionComponent> ent, ref InteractHandEvent args)
    {
        if (!ent.Comp.Jammed || IsBroken(ent.Comp))
            return;

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.UnjamTime, new GunConditionUnjamDoAfterEvent(), ent, target: ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
            RequireCanInteract = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("gun-condition-unjam-started"), ent, args.User);
    }

    private void OnInteractUsing(Entity<GunConditionComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Condition >= ent.Comp.MaxCondition)
            return;

        args.Handled = _tool.UseTool(
            args.Used,
            args.User,
            ent,
            ent.Comp.RepairTime,
            ent.Comp.RepairToolQuality,
            new GunConditionRepairDoAfterEvent());
    }

    private void OnRepairFinished(Entity<GunConditionComponent> ent, ref GunConditionRepairDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        ent.Comp.Condition = Math.Min(ent.Comp.MaxCondition, ent.Comp.Condition + ent.Comp.RepairAmount);
        Dirty(ent);

        if (args.Used is { } toolUid &&
            TryComp<GunConditionRepairToolComponent>(toolUid, out var repairTool))
        {
            ConsumeRepairTool(toolUid, repairTool);
        }

        _popup.PopupEntity(Loc.GetString("gun-condition-repair-finished"), ent, args.User);
    }

    private void OnUnjamFinished(Entity<GunConditionComponent> ent, ref GunConditionUnjamDoAfterEvent args)
    {
        if (args.Cancelled || !ent.Comp.Jammed || IsBroken(ent.Comp))
            return;

        ent.Comp.Jammed = false;
        Dirty(ent);

        _popup.PopupEntity(Loc.GetString("gun-condition-unjam-finished"), ent, args.User);
    }

    private void ConsumeRepairTool(EntityUid toolUid, GunConditionRepairToolComponent tool)
    {
        if (tool.Uses < 0)
            return;

        tool.Uses--;
        if (tool.Uses == 0)
        {
            QueueDel(toolUid);
            return;
        }

        Dirty(toolUid, tool);
    }
}
