using Content.Shared.Body.Systems;
using Content.Shared.Body.Components;

namespace Content.Shared.Body.Components;

[RegisterComponent, Access(typeof(BrainSystem))]
public sealed partial class BrainComponent : Component;
