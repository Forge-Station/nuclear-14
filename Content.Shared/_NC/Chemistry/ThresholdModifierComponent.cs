using System;
using System.Collections.Generic;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Server.ThresholdModifier;

[Serializable]
[DataDefinition]
public partial struct ModifierEntry
{
    [DataField] public float CritMultiplier;
    [DataField] public float DeathMultiplier;
    [DataField] public double EndTimeSeconds; // храним время в секундах (double)
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ThresholdModifierComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<ModifierEntry> Modifiers = new();

    [DataField, AutoNetworkedField]
    public FixedPoint2 OriginalCritThreshold;

    [DataField, AutoNetworkedField]
    public FixedPoint2 OriginalDeadThreshold;

    [DataField, AutoNetworkedField]
    public bool OriginalSaved;
}