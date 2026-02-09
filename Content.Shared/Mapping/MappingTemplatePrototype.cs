using Robust.Shared.Prototypes;

namespace Content.Shared.Mapping;

/// <summary>
/// This is a prototype for predefining the start content of the “templates” section in the map editor.
/// </summary>
[Prototype("mappingTemplate")]
public sealed partial class MappingTemplatePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;

    /// <summary>
    /// Used to allocate root objects to the corresponding sections of the map editor interface.
    /// </summary>
    [DataField]
    public TemplateType? RootType { get; set; }

    /// <summary>
    /// Prototypes for which this one will be a parent.
    /// </summary>
    [DataField]
    public List<MappingTemplatePrototype> Children { get; set; } = new ();
}

[Serializable]
public enum TemplateType : byte
{
    Tile,
    Decal,
    Entity,
}
