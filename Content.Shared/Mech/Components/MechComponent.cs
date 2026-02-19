using Content.Shared.FixedPoint;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Mech.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MechComponent : Component
{
    [DataField("breakOnEmag")]
    [AutoNetworkedField]
    public bool BreakOnEmag = true;
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public FixedPoint2 Integrity;
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public FixedPoint2 MaxIntegrity = 250;
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public FixedPoint2 Energy = 0;
    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public FixedPoint2 MaxEnergy = 0;
    [ViewVariables]
    public ContainerSlot BatterySlot = default!;
    [ViewVariables]
    public readonly string BatterySlotId = "mech-battery-slot";
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MechToPilotDamageMultiplier;
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool Broken = false;
    [ViewVariables(VVAccess.ReadWrite)]
    public ContainerSlot PilotSlot = default!;
    [ViewVariables]
    public readonly string PilotSlotId = "mech-pilot-slot";
    [ViewVariables, AutoNetworkedField]
    public EntityUid? CurrentSelectedEquipment;
    [DataField("maxEquipmentAmount"), ViewVariables(VVAccess.ReadWrite)]
    public int MaxEquipmentAmount = 3;
    [DataField]
    public EntityWhitelist? EquipmentWhitelist;

    [DataField]
    public EntityWhitelist? PilotWhitelist;
    [ViewVariables(VVAccess.ReadWrite)]
    public Container EquipmentContainer = default!;
    [ViewVariables]
    public readonly string EquipmentContainerId = "mech-equipment-container";
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float EntryDelay = 3;
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ExitDelay = 3;
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float BatteryRemovalDelay = 2;
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool Airtight;
    [DataField]
    public List<EntProtoId> StartingEquipment = new();
    #region Action Prototypes
    [DataField]
    public EntProtoId MechCycleAction = "ActionMechCycleEquipment";
    [DataField]
    public EntProtoId ToggleAction = "ActionToggleLight";
    [DataField]
    public EntProtoId MechUiAction = "ActionMechOpenUI";
    [DataField]
    public EntProtoId MechEjectAction = "ActionMechEject";
    [DataField]
    public EntProtoId MechHornAction = "ActionMechHorn";  // Forge-Change
    [DataField]
    public EntProtoId MechSirenAction = "ActionMechSiren";  // Forge-Change
    #endregion

    #region Visualizer States
    [DataField]
    public string? BaseState;
    [DataField]
    public string? OpenState;
    [DataField]
    public string? BrokenState;
    #endregion
    [DataField] public EntityUid? MechCycleActionEntity;
    [DataField] public EntityUid? MechUiActionEntity;
    [DataField] public EntityUid? MechEjectActionEntity;
 // Forge-Change-start
    [DataField, AutoNetworkedField] public EntityUid? ToggleActionEntity;
    [DataField, AutoNetworkedField] public EntityUid? MechHornActionEntity;
    [DataField, AutoNetworkedField] public EntityUid? MechSirenActionEntity;
    [DataField]
    public SoundSpecifier? HornSound;
    [DataField]
    public SoundSpecifier? SirenSound;
    [DataField]
    public string? EngineOnState;
    [DataField, AutoNetworkedField] public bool SirenEnabled;
    [DataField, AutoNetworkedField] public EntityUid? SirenStream;
    [ViewVariables]
    public ContainerSlot IgnitionSlot = default!;
    [ViewVariables]
    public readonly string IgnitionSlotId = "mech-ignition-slot";
    [DataField, AutoNetworkedField] public bool EngineRunning; // true when key is inserted
    [ViewVariables]
    public ContainerSlot PassengerSlot1 = default!;
    [ViewVariables]
    public readonly string PassengerSlot1Id = "mech-passenger-1";
    [ViewVariables]
    public ContainerSlot PassengerSlot2 = default!;
    [ViewVariables]
    public readonly string PassengerSlot2Id = "mech-passenger-2";
    [ViewVariables]
    public ContainerSlot PassengerSlot3 = default!;
    [ViewVariables]
    public readonly string PassengerSlot3Id = "mech-passenger-3";
    #region Action Prototypes
    [DataField]
    public EntProtoId MechEjectPassenger1Action = "ActionMechEjectPassenger1";
    [DataField]
    public EntProtoId MechEjectPassenger2Action = "ActionMechEjectPassenger2";
    [DataField]
    public EntProtoId MechEjectPassenger3Action = "ActionMechEjectPassenger3";
    #endregion

    #region Action Entities
    [DataField, AutoNetworkedField] public EntityUid? MechEjectPassenger1ActionEntity;
    [DataField, AutoNetworkedField] public EntityUid? MechEjectPassenger2ActionEntity;
    [DataField, AutoNetworkedField] public EntityUid? MechEjectPassenger3ActionEntity;
    #endregion
 // Forge-Change-end
}
