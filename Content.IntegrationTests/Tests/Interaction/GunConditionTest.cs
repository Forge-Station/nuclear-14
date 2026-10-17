/// Forge-Change-Start
using System.Numerics;
using Content.Shared._Forge.Weapons.Ranged.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Interaction;

public sealed class GunConditionTest : InteractionTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task UnjamTakesPriorityOverWielding(bool wielded)
    {
        await SpawnTarget("N14WeaponShotgun");
        await Pickup();
        var condition = Comp<GunConditionComponent>();
        var grip = Comp<WieldableComponent>();
        await Server.WaitPost(() =>
        {
            HandSys.AddHand(SPlayer, "second", HandLocation.Left);
            if (wielded)
                Assert.That(SEntMan.System<WieldableSystem>().TryWield(STarget!.Value, grip, SPlayer), Is.True);
            condition.Jammed = true;
            condition.UnjamTime = 2f;
            SEntMan.Dirty(STarget!.Value, condition);
        });
        await RunTicks(5);

        // Клиент не должен предсказывать смену хвата вместо устранения клина.
        await Client.WaitPost(() =>
        {
            var clientGrip = CEntMan.GetComponent<WieldableComponent>(CTarget!.Value);
            Assert.That(clientGrip.Wielded, Is.EqualTo(wielded));
            var ev = new UseInHandEvent(CPlayer);
            CEntMan.EventBus.RaiseLocalEvent(CTarget.Value, ev);
            Assert.That(ev.Handled, Is.True);
            Assert.That(clientGrip.Wielded, Is.EqualTo(wielded));
        });

        await Server.WaitPost(() => InteractSys.UseInHandInteraction(SPlayer, STarget!.Value, checkUseDelay: false));
        Assert.That(SEntMan.HasComponent<GunUnjammingComponent>(SPlayer), Is.True);
        Assert.That(grip.Wielded, Is.EqualTo(wielded));
        await AwaitDoAfters();
        Assert.That(condition.Jammed, Is.False);
        Assert.That(grip.Wielded, Is.EqualTo(wielded));

        // После устранения клина обычное переключение хвата снова доступно.
        await Server.WaitPost(() => InteractSys.UseInHandInteraction(SPlayer, STarget!.Value, checkUseDelay: false));
        Assert.That(grip.Wielded, Is.EqualTo(!wielded));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task UnjamAllowsMovementButCancelsOnDamageOrDrop(bool cancelByDropping, bool takeDamage)
    {
        await SpawnTarget("ForgeWeaponRifleGestio");
        await Pickup();
        var condition = Comp<GunConditionComponent>();
        MovementSpeedModifierComponent speed = null!;
        await Server.WaitPost(() =>
        {
            speed = SEntMan.EnsureComponent<MovementSpeedModifierComponent>(SPlayer);
            SEntMan.EnsureComponent<DamageableComponent>(SPlayer);
            condition.Jammed = true;
            condition.UnjamTime = 2f;
            InteractSys.UseInHandInteraction(SPlayer, STarget!.Value);
        });
        Assert.That(SEntMan.HasComponent<GunUnjammingComponent>(SPlayer), Is.True);
        Assert.That(speed.CurrentSprintSpeed, Is.EqualTo(speed.CurrentWalkSpeed).Within(0.001f));

        await Server.WaitPost(() =>
        {
            var transform = SEntMan.GetComponent<TransformComponent>(SPlayer);
            SEntMan.System<SharedTransformSystem>().SetCoordinates(SPlayer, transform.Coordinates.Offset(new Vector2(1f, 0f)));
            if (takeDamage)
            {
                var hit = new DamageSpecifier();
                hit.DamageDict.Add("Blunt", FixedPoint2.New(10));
                SEntMan.System<DamageableSystem>().TryChangeDamage(SPlayer, hit, ignoreResistances: true);
            }
        });
        await RunTicks(5);
        Assert.That(SEntMan.HasComponent<GunUnjammingComponent>(SPlayer), Is.EqualTo(!takeDamage));
        if (cancelByDropping)
            await Drop();
        await AwaitDoAfters();
        await RunTicks(5);
        Assert.That(condition.Jammed, Is.EqualTo(cancelByDropping || takeDamage));
        Assert.That(SEntMan.HasComponent<GunUnjammingComponent>(SPlayer), Is.False);
        Assert.That(speed.CurrentSprintSpeed, Is.EqualTo(speed.BaseSprintSpeed).Within(0.001f));
    }

    [Test]
    public async Task DirectDamageAndRepair()
    {
        await SpawnTarget("ForgeWeaponRifleGestio");
        var condition = Comp<GunConditionComponent>();
        var damageable = Comp<DamageableComponent>();
        var damageSystem = SEntMan.System<DamageableSystem>();
        var hit = new DamageSpecifier();
        hit.DamageDict.Add("Blunt", FixedPoint2.New(200));

        await Server.WaitPost(() => damageSystem.TryChangeDamage(STarget, hit, ignoreResistances: true));
        Assert.That(condition.Condition, Is.EqualTo(0f));
        Assert.That(condition.Jammed, Is.True);

        await InteractUsing("N14ToolboxWeaponMaintenance");
        Assert.That(condition.Condition, Is.EqualTo(40f));
        Assert.That(damageable.TotalDamage, Is.LessThan(FixedPoint2.New(200)));

        hit.DamageDict["Blunt"] = FixedPoint2.New(10);
        await Server.WaitPost(() => damageSystem.TryChangeDamage(STarget, hit, ignoreResistances: true));
        Assert.That(condition.Condition, Is.EqualTo(30f));
    }

    [TestCase("N14JunkDuctTape")]
    [TestCase("N14JunkWonderglue")]
    public async Task ImprovisedRepairConsumesMaterial(string material)
    {
        await SpawnTarget("ForgeWeaponRifleGestio");
        var condition = Comp<GunConditionComponent>();
        await Server.WaitPost(() => condition.Condition = 100f);
        await InteractUsing(material);
        Assert.That(condition.Condition, Is.EqualTo(104.5f).Within(0.001f));
        Assert.That(Hands.ActiveHandEntity, Is.Null);
    }

    [Test]
    public async Task CancelledRepairDoesNotConsumeMaterial()
    {
        await SpawnTarget("ForgeWeaponRifleGestio");
        var condition = Comp<GunConditionComponent>();
        await Server.WaitPost(() => condition.Condition = 100f);
        await InteractUsing("N14JunkDuctTape", awaitDoAfters: false);
        await CancelDoAfters();
        Assert.That(condition.Condition, Is.EqualTo(100f));
        Assert.That(Hands.ActiveHandEntity, Is.Not.Null);
        Assert.That(SEntMan.GetComponent<GunConditionRepairToolComponent>(Hands.ActiveHandEntity!.Value).Uses, Is.EqualTo(1));
    }

    [TestCase("N14Wrench", 44f, 44f)]
    [TestCase("N14Wrench", 45f, 48f)]
    [TestCase("N14Wrench", 89f, 90f)]
    [TestCase("N14Wrench", 90f, 90f)]
    [TestCase("N14Welder", 89f, 89f)]
    [TestCase("N14Welder", 90f, 93f)]
    [TestCase("N14Welder", 134f, 135f)]
    [TestCase("N14Welder", 135f, 135f)]
    public async Task RepairStagesRespectBounds(string tool, float initial, float expected)
    {
        await SpawnTarget("ForgeWeaponRifleGestio");
        var condition = Comp<GunConditionComponent>();
        await Server.WaitPost(() => condition.Condition = initial);
        await InteractUsing(tool);
        Assert.That(condition.Condition, Is.EqualTo(expected).Within(0.001f));
        Assert.That(Hands.ActiveHandEntity, Is.Not.Null);
    }

    [Test]
    public async Task WeldingRepairConsumesFuel()
    {
        await SpawnTarget("ForgeWeaponRifleGestio");
        var condition = Comp<GunConditionComponent>();
        await Server.WaitPost(() => condition.Condition = 90f);
        await PlaceInHands("N14Welder");
        var welder = Hands.ActiveHandEntity!.Value;
        var before = ToolSys.GetWelderFuelAndCapacity(welder).fuel;
        await Interact();
        var after = ToolSys.GetWelderFuelAndCapacity(welder).fuel;
        Assert.That(condition.Condition, Is.EqualTo(93f).Within(0.001f));
        Assert.That(before - after, Is.GreaterThanOrEqualTo(FixedPoint2.New(1)));
    }

    [Test]
    public async Task UnlitWelderCannotRepair()
    {
        await SpawnTarget("ForgeWeaponRifleGestio");
        var condition = Comp<GunConditionComponent>();
        await Server.WaitPost(() => condition.Condition = 90f);
        await PlaceInHands("N14Welder", enableToggleable: false);
        await Interact();
        Assert.That(condition.Condition, Is.EqualTo(90f));
    }

    [Test]
    public async Task ScrewdriverRepairHasLimit()
    {
        await SpawnTarget("ForgeWeaponRifleGestio");
        var condition = Comp<GunConditionComponent>();
        await Server.WaitPost(() => condition.Condition = 44f);
        await InteractUsing("N14Screwdriver");
        Assert.That(condition.Condition, Is.EqualTo(45f).Within(0.001f));
        Assert.That(Hands.ActiveHandEntity, Is.Not.Null);
        await Interact();
        Assert.That(condition.Condition, Is.EqualTo(45f).Within(0.001f));
    }
}
/// Forge-Change-End
