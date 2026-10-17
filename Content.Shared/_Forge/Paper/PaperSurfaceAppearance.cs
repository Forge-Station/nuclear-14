using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Paper;

/// <summary>Physical sheet appearance, independent of its printed contents and layout.</summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class PaperSurfaceAppearance
{
    [DataField] public int Seed;
    [DataField] public string Profile = "N14WornNewsprint";
    [DataField] public float Wear = 0.65f;
    /// <summary>Material texture, independent of damage. Zero disables each effect.</summary>
    [DataField] public float Fibers = 1f;
    [DataField] public float Impurities = 1f;
    public PaperSurfaceAppearance Clone() => (PaperSurfaceAppearance)MemberwiseClone();
}

[Prototype("paperSurface")]
public sealed partial class PaperSurfacePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public Color BaseColor = Color.FromHex("#DDDFD9");
    [DataField] public float Grain = 0.018f;
    [DataField] public float Discoloration = 0.035f;
    [DataField] public float EdgeWear = 0.12f;
    [DataField] public float CreaseStrength = 0.09f;
    [DataField] public int Creases = 2;
    [DataField] public int Smudges = 7;
    [DataField] public float EdgeRoughness = 0.006f;
    [DataField] public float CornerWear = 0.018f;
    [DataField] public int Wrinkles = 6;
    [DataField] public int FiberCount = 1600;
    [DataField] public float FiberStrength = 0.09f;
    [DataField] public int ImpurityCount = 650;
    [DataField] public float ImpurityStrength = 0.12f;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PaperSurfaceComponent : Component
{
    [DataField, AutoNetworkedField] public PaperSurfaceAppearance Appearance = new();
}
