using Content.Shared.Examine;
using Content.Shared.Abilities.Psionics;
using Content.Shared.Humanoid;
using Content.Shared.Psionics;
using Content.Shared.Bed.Sleep;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Shadowkin;
using Content.Shared.Rejuvenate;
using Content.Shared.Alert;
using Content.Shared.Rounding;
using Content.Shared.Actions;
using Robust.Shared.Prototypes;
using Content.Server.Abilities.Psionics;
using Content.Server.Damage.Systems;
using Content.Shared.Body;

namespace Content.Server.Shadowkin;

public sealed class ShadowkinSystem : EntitySystem
{
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly SharedVisualBodySystem _visualBody = default!; // Forge-Change
    [Dependency] private readonly PsionicAbilitiesSystem _psionicAbilitiesSystem = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public const string ShadowkinSleepActionId = "ShadowkinActionSleep";
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShadowkinComponent, ComponentStartup>(OnInit);
        SubscribeLocalEvent<ShadowkinComponent, OnMindbreakEvent>(OnMindbreak);
        SubscribeLocalEvent<ShadowkinComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<ShadowkinComponent, EyeColorInitEvent>(OnEyeColorChange);
    }

    private void OnInit(EntityUid uid, ShadowkinComponent component, ComponentStartup args)
    {
        _actionsSystem.AddAction(uid, ref component.ShadowkinSleepAction, ShadowkinSleepActionId, uid);
    }

    private void OnEyeColorChange(EntityUid uid, ShadowkinComponent component, EyeColorInitEvent args)
    {
        // Forge-Change: цвет глаз переехал в профиль органа-глаза.
        if (!_visualBody.TryGetEyeColor(uid, out var eyeColor) || eyeColor == component.OldEyeColor)
            return;

        component.OldEyeColor = eyeColor;
    }

    private void OnMindbreak(EntityUid uid, ShadowkinComponent component, ref OnMindbreakEvent args)
    {
        if (TryComp<MindbrokenComponent>(uid, out var mindbreak))
            mindbreak.MindbrokenExaminationText = "examine-mindbroken-shadowkin-message";

        if (_visualBody.TryGetEyeColor(uid, out var oldEyeColor)) // Forge-Change
        {
            component.OldEyeColor = oldEyeColor;
            _visualBody.SetEyeColor(uid, component.BlackEyeColor);
        }

        if (TryComp<StaminaComponent>(uid, out var stamina))
            _stamina.TakeStaminaDamage(uid, stamina.CritThreshold, stamina, uid);
    }

    private void OnRejuvenate(EntityUid uid, ShadowkinComponent component, RejuvenateEvent args)
    {
        if (!HasComp<MindbrokenComponent>(uid))
            return;

        RemComp<MindbrokenComponent>(uid);

        _visualBody.SetEyeColor(uid, component.OldEyeColor); // Forge-Change

        EnsureComp<PsionicComponent>(uid, out _);
        if (_prototypeManager.TryIndex<PsionicPowerPrototype>("ShadowkinPowers", out var shadowkinPowers))
            _psionicAbilitiesSystem.InitializePsionicPower(uid, shadowkinPowers);
    }
}
