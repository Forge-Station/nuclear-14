using Robust.Shared.Prototypes;

namespace Content.Shared.Overworld.Components;

/// <summary>
/// Предмет или терминал — активация переводит игрока в Overworld.
/// </summary>
[RegisterComponent]
public sealed partial class WorldMapActivatorComponent : Component
{
    /// <summary>
    /// true = должен быть в руках (UseInHand).
    /// false = стационарный терминал (ActivateInWorld/verb).
    /// </summary>
    [DataField]
    public bool RequiresHolding = false;

    /// <summary>
    /// ID узла на глобальной карте (например, TravelDest_Yuma).
    /// Если указан, токен заспавнится прямо на этом узле глобалки.
    /// </summary>
    [DataField]
    public ProtoId<TravelDestinationPrototype>? LinkedDestination;
}
