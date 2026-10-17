using Content.Shared.Speech.EntitySystems;
using Robust.Shared.GameStates;
using Content.Shared.Speech.Components;

namespace Content.Shared.Speech.Components;

/// <summary>
/// Lithping!
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(FrontalLispSystem))]
public sealed partial class FrontalLispComponent : BaseAccentComponent;
