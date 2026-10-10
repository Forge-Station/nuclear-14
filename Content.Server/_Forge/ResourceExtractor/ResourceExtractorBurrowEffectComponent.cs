using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.ResourceExtractor;

/// <summary>
/// Server-only delayed delivery for an extractor spawn event. The visual entity
/// exists first, so players get a fair warning before the creature appears.
/// </summary>
[RegisterComponent]
public sealed partial class ResourceExtractorBurrowEffectComponent : Component
{
    public EntProtoId SpawnPrototype;
    public EntityCoordinates SpawnCoordinates;
    public TimeSpan SpawnAt;
}
