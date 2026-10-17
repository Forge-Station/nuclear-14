using Content.Shared.Customization.Systems;
using Content.Shared.Roles;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization;

namespace Content.Shared.Traits;


/// <summary>
///     Describes a trait.
/// </summary>
[Prototype("trait")]
public sealed partial class TraitPrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Which customization tab to place this entry in
    /// </summary>
    /// <remarks>
    /// Апстримный код сравнивает категорию с null, поэтому поле обнуляемое;
    /// по умолчанию трейт всё так же попадает в "Uncategorized".
    /// </remarks>
    [DataField]
    public ProtoId<TraitCategoryPrototype>? Category = "Uncategorized";

    /// <summary>
    ///     How many points this will give the character
    /// </summary>
    [DataField]
    public int Points = 0;

    // Апстрим независимо сделал свою систему трейтов. Прототип в дереве один,
    // поэтому поля обеих систем живут рядом: форковый редактор персонажа читает
    // Points и Requirements, апстримный - Name и Cost.
    /// <summary>
    ///     Name of the trait displayed in the UI.
    /// </summary>
    [DataField]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>
    ///     Description of the trait displayed in the UI.
    /// </summary>
    [DataField]
    public LocId? Description { get; private set; }

    /// <summary>
    ///     Trait price. If negative, points are added instead.
    /// </summary>
    [DataField]
    public int Cost = 0;

    /// <summary>
    ///     Who may take this trait.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <inheritdoc cref="Whitelist"/>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    ///     Components added to the player when they pick this trait.
    /// </summary>
    [DataField]
    [AlwaysPushInheritance]
    public ComponentRegistry Components { get; private set; } = new();

    /// <summary>
    ///     Job specials run when the player picks this trait.
    /// </summary>
    [DataField(serverOnly: true)]
    public List<JobSpecial> Specials { get; private set; } = new();

    /// <summary>
    ///     Gear given to the player when they pick this trait.
    /// </summary>
    [DataField]
    public EntProtoId? TraitGear;


    [DataField]
    public List<CharacterRequirement> Requirements = new();

    [DataField(serverOnly: true)]
    public TraitFunction[] Functions { get; private set; } = Array.Empty<TraitFunction>();
}

/// This serves as a hook for trait functions to modify a player character upon spawning in.
[ImplicitDataDefinitionForInheritors]
public abstract partial class TraitFunction
{
    public abstract void OnPlayerSpawn(
        EntityUid mob,
        IComponentFactory factory,
        IEntityManager entityManager,
        ISerializationManager serializationManager);
}
