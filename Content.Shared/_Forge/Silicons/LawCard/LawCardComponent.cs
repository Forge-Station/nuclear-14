using Content.Shared.DoAfter;
using Content.Shared.Silicons.Laws;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Silicons.LawCard;

[RegisterComponent]
public sealed partial class LawCardComponent : Component
{
    public const int MaxLaws = 15;

    [DataField]
    public List<SiliconLaw> Laws = new();

    [DataField]
    public float UploadDelay = 5f;

    // если true, при заливке законов цель ещё и перенимает фракцию и допуски прошившего. если false — только законы.
    [DataField]
    public bool OverwriteAccess = true;
}

[Serializable, NetSerializable]
public sealed partial class LawCardDoAfterEvent : SimpleDoAfterEvent
{
}
