using Robust.Shared.Prototypes;

namespace Content.Shared.Overworld.Components;

/// <summary>
/// Точка перехода на Overworld-гриде.
/// Игрок взаимодействует токеном → тело телепортируется в destination.
/// </summary>
[RegisterComponent]
public sealed partial class TravelNodeComponent : Component
{
    [DataField(required: true)]
    public ProtoId<TravelDestinationPrototype> Destination;

    [DataField]
    public string DisplayName = "Unknown Location";

    // --- HOOK POINT ---
    // EncounterTable, DangerLevel, RequiredItems, IsLocked, etc.
}
