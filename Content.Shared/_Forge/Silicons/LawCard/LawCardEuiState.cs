using Content.Shared.Eui;
using Content.Shared.Silicons.Laws;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Silicons.LawCard;

[Serializable, NetSerializable]
public sealed class LawCardEuiState : EuiStateBase
{
    public List<SiliconLaw> Laws { get; }

    public LawCardEuiState(List<SiliconLaw> laws)
    {
        Laws = laws;
    }
}

[Serializable, NetSerializable]
public sealed class LawCardSaveMessage : EuiMessageBase
{
    public List<SiliconLaw> Laws { get; }

    public LawCardSaveMessage(List<SiliconLaw> laws)
    {
        Laws = laws;
    }
}
