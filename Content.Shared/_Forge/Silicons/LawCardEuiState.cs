using Content.Shared.Eui;
using Content.Shared.Silicons.Laws;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Silicons;

/// <summary>
/// Forge-Change: state for the law card editor EUI — the laws currently written on the card.
/// </summary>
[Serializable, NetSerializable]
public sealed class LawCardEuiState : EuiStateBase
{
    public List<SiliconLaw> Laws { get; }

    public LawCardEuiState(List<SiliconLaw> laws)
    {
        Laws = laws;
    }
}

/// <summary>
/// Forge-Change: sent from the editor when the player saves — the new set of laws to store on the card.
/// </summary>
[Serializable, NetSerializable]
public sealed class LawCardSaveMessage : EuiMessageBase
{
    public List<SiliconLaw> Laws { get; }

    public LawCardSaveMessage(List<SiliconLaw> laws)
    {
        Laws = laws;
    }
}
