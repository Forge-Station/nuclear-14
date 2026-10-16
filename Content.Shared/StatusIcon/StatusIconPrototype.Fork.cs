using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared.StatusIcon;

/// <summary>
/// Fork-owned status icon prototypes. They used to live in StatusIconPrototype.cs,
/// which upstream rewrote, so they are kept in a separate file to survive future merges.
/// </summary>

/// <summary>
/// StatusIcons for showing the psionics status on the epi HUD.
/// </summary>
[Prototype]
public sealed partial class PsionicsIconPrototype : StatusIconData, IPrototype, IInheritingPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <inheritdoc />
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<PsionicsIconPrototype>))]
    public string[]? Parents { get; private set; }

    /// <inheritdoc />
    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }
}

/// <summary>
/// StatusIcons for faction membership.
/// </summary>
[Prototype]
public sealed partial class FactionIconPrototype : StatusIconData, IPrototype, IInheritingPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <inheritdoc />
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<FactionIconPrototype>))]
    public string[]? Parents { get; private set; }

    /// <inheritdoc />
    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }
}
