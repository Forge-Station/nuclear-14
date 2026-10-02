using Content.Server.DoAfter;
using Content.Shared._Forge.Weapons.Ranged.Components;
using Content.Shared._Forge.Weapons.Ranged.Systems;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._Forge.Weapons.Ranged.Systems;

public sealed class GunConditionSystem : SharedGunConditionSystem
{
    private static readonly SoundSpecifier UnjamFallbackSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Cock/smg_cock.ogg");

    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunComponent, ComponentStartup>(OnGunStartup);
        SubscribeLocalEvent<GunConditionRepairToolComponent, ComponentStartup>(OnRepairToolStartup);
        SubscribeLocalEvent<GunConditionRepairToolComponent, ExaminedEvent>(OnRepairToolExamined);
        SubscribeLocalEvent<GunConditionComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<GunConditionComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<GunConditionComponent, UseInHandEvent>(OnUseInHand, before: new[] { typeof(SharedGunSystem) });
        SubscribeLocalEvent<GunConditionComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<GunConditionComponent, GunConditionRepairDoAfterEvent>(OnRepairFinished);
        SubscribeLocalEvent<GunConditionComponent, GunConditionUnjamDoAfterEvent>(OnUnjamFinished);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GunUnjammingComponent>();
        while (query.MoveNext(out var uid, out var unjamming))
        {
            // Также снимаем замедление, если оружие удалили до доставки события DoAfter.
            if (_doAfter.GetStatus(unjamming.DoAfter) != DoAfterStatus.Running)
                RemCompDeferred<GunUnjammingComponent>(uid);
        }
    }

    private void OnDamageChanged(Entity<GunConditionComponent> ent, ref DamageChangedEvent args)
    {
        // Износ остаётся в GunCondition. Учитываем только новый урон, а не накопленный итог.
        if (args.DamageDelta == null)
            return;

        var damage = 0f;
        foreach (var amount in args.DamageDelta.DamageDict.Values)
        {
            if (amount > 0)
                damage += amount.Float();
        }

        if (damage <= 0f)
            return;

        ent.Comp.Condition = Math.Max(ent.Comp.BrokenThreshold, ent.Comp.Condition - damage);
        if (IsBroken(ent.Comp) && !ent.Comp.Jammed)
        {
            ent.Comp.Jammed = true;
            SetBoltForJammed(ent, true);
        }
        Dirty(ent);
    }

    private void OnGunStartup(EntityUid uid, GunComponent component, ref ComponentStartup args)
    {
        if (!HasComp<ItemComponent>(uid))
            return;

        EnsureComp<GunConditionComponent>(uid);
    }

    private void OnRepairToolExamined(Entity<GunConditionRepairToolComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || ent.Comp.RepairFraction is not { } fraction)
            return;

        args.PushMarkup(Loc.GetString("gun-condition-repair-tool-examine",
            ("amount", $"{fraction * 100f:0}"),
            ("minimum", $"{ent.Comp.RepairMinimum * 100f:0}"),
            ("limit", $"{ent.Comp.RepairLimit * 100f:0}")));
        if (ent.Comp.Uses > 0)
            args.PushMarkup(Loc.GetString("gun-condition-repair-tool-uses", ("uses", ent.Comp.Uses)));
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

        var wasJammed = ent.Comp.Jammed;

        var shotsFired = Math.Max(args.Ammo.Count, 1);
        var wearAmount = ent.Comp.WearPerShot * shotsFired;
        ent.Comp.Condition = Math.Max(ent.Comp.BrokenThreshold, ent.Comp.Condition - wearAmount);

        if (IsBroken(ent.Comp))
        {
            ent.Comp.Jammed = true;
            if (!wasJammed)
                SetBoltForJammed(ent.Owner, true);
            Dirty(ent);
            return;
        }

        if (!ent.Comp.Jammed && _random.Prob(GetJamChance(ent.Comp)))
            ent.Comp.Jammed = true;

        if (!wasJammed && ent.Comp.Jammed)
            SetBoltForJammed(ent.Owner, true);

        Dirty(ent);
    }

    private void OnUseInHand(Entity<GunConditionComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !ent.Comp.Jammed || IsBroken(ent.Comp))
            return;

        args.Handled = true;
        StartUnjam(ent, args.User);
    }

    private void OnInteractUsing(Entity<GunConditionComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Condition >= ent.Comp.MaxCondition)
            return;

        if (TryComp<GunConditionRepairToolComponent>(args.Used, out var repairTool) && repairTool.RepairFraction != null)
        {
            args.Handled = true;
            if (repairTool.Uses == 0 || GetRepairAmount(ent.Comp, repairTool) <= 0f)
            {
                _popup.PopupEntity(Loc.GetString("gun-condition-repair-limit"), ent, args.User);
                return;
            }

            if (repairTool.RepairQuality is { } quality)
            {
                _tool.UseTool(args.Used, args.User, ent, repairTool.RepairTime, quality,
                    new GunConditionRepairDoAfterEvent(), repairTool.RepairFuel);
                return;
            }

            _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, repairTool.RepairTime,
                new GunConditionRepairDoAfterEvent(), ent, target: ent, used: args.Used)
            {
                BreakOnDamage = true,
                BreakOnMove = true,
                NeedHand = true,
                DistanceThreshold = 2f,
            });
            return;
        }

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
        if (args.Cancelled || args.Handled || args.Used is not { } toolUid || TerminatingOrDeleted(toolUid))
            return;

        TryComp<GunConditionRepairToolComponent>(toolUid, out var repairTool);
        if (repairTool is { Uses: 0 })
            return;

        var amount = GetRepairAmount(ent.Comp, repairTool);
        if (amount <= 0f)
            return;

        args.Handled = true;
        // Уменьшаем накопленный урон в той же пропорции, в которой устранили износ.
        if (TryComp<DamageableComponent>(ent, out var damageable))
        {
            var fraction = amount / (ent.Comp.MaxCondition - ent.Comp.Condition);
            _damage.TryChangeDamage(ent, damageable.Damage * -fraction, ignoreResistances: true,
                interruptsDoAfters: false);
        }

        ent.Comp.Condition += amount;
        Dirty(ent);

        if (repairTool != null)
            ConsumeRepairTool(toolUid, repairTool);

        _popup.PopupEntity(Loc.GetString("gun-condition-repair-finished"), ent, args.User);
    }

    private static float GetRepairAmount(GunConditionComponent gun, GunConditionRepairToolComponent? tool)
    {
        if (gun.Condition < gun.MaxCondition * Math.Clamp(tool?.RepairMinimum ?? 0f, 0f, 1f))
            return 0f;

        var limit = gun.MaxCondition * Math.Clamp(tool?.RepairLimit ?? 1f, 0f, 1f);
        var amount = tool?.RepairFraction is { } fraction ? gun.MaxCondition * fraction : gun.RepairAmount;
        return Math.Max(0f, Math.Min(amount, limit - gun.Condition));
    }

    private void OnUnjamFinished(Entity<GunConditionComponent> ent, ref GunConditionUnjamDoAfterEvent args)
    {
        if (TryComp<GunUnjammingComponent>(args.User, out var unjamming) &&
            unjamming.DoAfter == new DoAfterId(args.User, args.DoAfter.Index))
            RemComp<GunUnjammingComponent>(args.User);

        if (args.Cancelled || !ent.Comp.Jammed || IsBroken(ent.Comp))
            return;

        ent.Comp.Jammed = false;
        SetBoltForJammed(ent.Owner, false);
        Dirty(ent);

        PlayUnjamSound(ent, args.User);
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

    private void StartUnjam(Entity<GunConditionComponent> ent, EntityUid user)
    {
        var doAfter = new DoAfterArgs(EntityManager, user, ent.Comp.UnjamTime, new GunConditionUnjamDoAfterEvent(), ent, target: ent)
        {
            BreakOnDamage = false,
            BreakOnMove = false,
            NeedHand = true,
            RequireCanInteract = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter, out var id))
            return;

        if (_doAfter.GetStatus(id) == DoAfterStatus.Running)
            EnsureComp<GunUnjammingComponent>(user).DoAfter = id;

        _popup.PopupEntity(Loc.GetString("gun-condition-unjam-started"), ent, user);
    }

    private void PlayUnjamSound(EntityUid gunUid, EntityUid user)
    {
        SoundSpecifier? sound = null;

        if (TryComp<ChamberMagazineAmmoProviderComponent>(gunUid, out var chamber))
        {
            if (chamber.BoltClosed != null)
                return;
            sound = chamber.RackSound ?? chamber.BoltClosedSound ?? chamber.BoltOpenedSound;
        }

        if (sound == null && TryComp<BallisticAmmoProviderComponent>(gunUid, out var ballistic))
            sound = ballistic.SoundRack;

        sound ??= UnjamFallbackSound;
        _audio.PlayPredicted(sound, gunUid, user);
    }

    private void SetBoltForJammed(EntityUid gunUid, bool jammed)
    {
        if (!TryComp<ChamberMagazineAmmoProviderComponent>(gunUid, out var chamber) ||
            chamber.BoltClosed == null)
        {
            return;
        }

        _gun.SetBoltClosed(gunUid, chamber, !jammed, user: null);
    }
}
