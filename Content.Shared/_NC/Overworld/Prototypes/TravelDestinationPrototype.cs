using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;

namespace Content.Shared.Overworld;

[Prototype("travelDestination")]
public sealed partial class TravelDestinationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// MarkerID для поиска OverworldArrivalMarkerComponent на целевом гриде.
    /// </summary>
    [DataField]
    public string? ArrivalMarkerTag = null;

    /// <summary>
    /// Путь к .yml карте. Если карта не загружена — загрузится автоматически при первом переходе.
    /// </summary>
    [DataField]
    public ResPath? MapPath = null;

    // ── Instance (TODO Этап 2) ────────────────────────────────────────────────
    [DataField]
    public List<ResPath> InstanceMapVariants = new();

    public TravelDestinationType DestinationType =>
        InstanceMapVariants.Count > 0
            ? TravelDestinationType.Instance
            : TravelDestinationType.Static;
}

public enum TravelDestinationType : byte
{
    Static,
    Instance,
}
