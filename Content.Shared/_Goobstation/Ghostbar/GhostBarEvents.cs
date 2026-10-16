using Robust.Shared.Serialization;

namespace Content.Shared.Ghost;

/// <summary>
/// Goobstation - A client to server request to spawn at the ghost bar.
/// Previously declared in Content.Shared/Ghost/SharedGhostSystem.cs, which upstream moved and rewrote.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostBarSpawnEvent : EntityEventArgs
{
}

/// <summary>
/// A client to server request to return to the round from the ghost state.
/// Previously declared in Content.Shared/Ghost/SharedGhostSystem.cs, which upstream moved and rewrote.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostReturnToRoundRequest : EntityEventArgs;
