using Robust.Shared.GameStates;
using Content.Shared.Forensics.Components;

namespace Content.Shared.Forensics.Components;

/// <summary>
/// This component is for entities we do not wish to track fingerprints/fibers, like puddles
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IgnoresFingerprintsComponent : Component;
