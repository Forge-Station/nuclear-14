using Robust.Shared.GameStates;
using Content.Shared.Trigger.Components;

namespace Content.Shared.Trigger.Components;

/// <summary>
/// Component used for tracking active timers triggers.
/// Used internally for performance reasons.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ActiveTimerTriggerComponent : Component;
