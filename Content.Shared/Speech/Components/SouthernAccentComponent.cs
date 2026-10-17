using Content.Shared.Speech.EntitySystems;
using Robust.Shared.GameStates;
using Content.Shared.Speech.Components;

namespace Content.Shared.Speech.Components;

/// <summary>
/// Dagnabit.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SouthernAccentSystem))]
public sealed partial class SouthernAccentComponent : BaseAccentComponent;
