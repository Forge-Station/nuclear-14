using System.Collections.Generic;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.Forge;

/// <summary>
///     BUI key for the armor-assembly minigame opened on the mannequin.
/// </summary>
[Serializable, NetSerializable]
public enum N14ForgeMiniGameUiKey : byte
{
    Key,
}

/// <summary>
///     Server -> client state: which part icons the minigame board should show.
/// </summary>
[Serializable, NetSerializable]
public sealed class N14ForgeMiniGameState : BoundUserInterfaceState
{
    /// <summary>
    ///     Prototype of the torso piece (always drawn in the center).
    /// </summary>
    public string TorsoProto = string.Empty;

    /// <summary>
    ///     Prototypes of the 4 limb pieces (arm/leg, left/right) to be dragged to the torso.
    /// </summary>
    public List<string> PartProtos = new();
}

/// <summary>
///     Client -> server: the player dragged all pieces onto the torso, armor should be claimed.
/// </summary>
[Serializable, NetSerializable]
public sealed class N14ForgeMiniGameCompletedMessage : BoundUserInterfaceMessage
{
}