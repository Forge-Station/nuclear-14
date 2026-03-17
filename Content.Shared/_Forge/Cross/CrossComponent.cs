using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Cross;

[RegisterComponent]
public sealed partial class CrossComponent : Component
{
    [DataField("hangDelay")]
    public TimeSpan HangDelay = TimeSpan.FromSeconds(20);

    [DataField("unhangDelay")]
    public TimeSpan UnhangDelay = TimeSpan.FromSeconds(20);

    [DataField("restraintPrototype")]
    public EntProtoId RestraintPrototype = "CrossRestraints";

    [DataField("occupiedOverlayPrototype")]
    public EntProtoId OccupiedOverlayPrototype = "CrossOccupiedOverlay";

    [DataField("unstrapDistance")]
    public float UnstrapDistance = 0.85f;

    [DataField("northBuckleOffset")]
    public Vector2 NorthBuckleOffset = new(0.08f, 0.25f);

    [DataField("southBuckleOffset")]
    public Vector2 SouthBuckleOffset = new(0.03f, 0.23f);

    [DataField("eastBuckleOffset")]
    public Vector2 EastBuckleOffset = new(0.24f, 0.22f);

    [DataField("westBuckleOffset")]
    public Vector2 WestBuckleOffset = new(-0.24f, 0.22f);

    [DataField("breakStunDuration")]
    public TimeSpan BreakStunDuration = TimeSpan.FromSeconds(4);

    [DataField("breakDamage")]
    public DamageSpecifier BreakDamage = new() { DamageDict = new() { ["Blunt"] = 10 } };

    [ViewVariables]
    public bool BreakInProgress;

    [ViewVariables]
    public EntityUid? HungTarget;

    [ViewVariables]
    public CrossActionState ActionState = CrossActionState.Idle;

    [ViewVariables]
    public TimeSpan? ActionDeadline;

    [ViewVariables]
    public EntityUid? ActiveUser;

    [ViewVariables]
    public EntityUid? ActiveTarget;

    [ViewVariables]
    public uint ActionId;

    public Vector2 GetBuckleOffset(Direction direction, Vector2 fallback)
    {
        return direction switch
        {
            Direction.North => NorthBuckleOffset,
            Direction.South => SouthBuckleOffset,
            Direction.East => EastBuckleOffset,
            Direction.West => WestBuckleOffset,
            _ => fallback
        };
    }
}

public enum CrossActionState : byte
{
    Idle = 0,
    HangPending,
    UnhangPending
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HungOnCrossComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Cross;
}

[Serializable, NetSerializable]
public sealed partial class CrossHangDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public NetEntity HangTarget;

    [DataField]
    public uint ActionId;
}

[Serializable, NetSerializable]
public sealed partial class CrossUnhangDoAfterEvent : SimpleDoAfterEvent
{
    [DataField]
    public NetEntity UnhangTarget;

    [DataField]
    public uint ActionId;
}
