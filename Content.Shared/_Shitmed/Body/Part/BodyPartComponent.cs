using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Body.Part;

/// <summary>
/// Shitmed's body part data, carried alongside upstream's <see cref="OrganComponent"/> on
/// limb entities.
///
/// Fork-owned. Upstream's Nubody rewrite removed Content.Shared/Body/Part: limbs are now
/// organs in a body's <c>body_organs</c> container, related through
/// <see cref="ParentOrganComponent"/>/<see cref="ChildOrganComponent"/> and keyed by
/// <see cref="OrganCategoryPrototype"/>. Everything below is Shitmed's own: part
/// integrity and severing, self-healing, surgery tool behaviour, species sprites.
///
/// The old slot dictionaries (<c>ParentSlot</c>, <c>Children</c>, <c>Organs</c>) are gone -
/// nothing in Shitmed read them, and the organ graph replaces them.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BodyPartComponent : Component, ISurgeryToolComponent
{
    /// <summary>
    /// Parent body for this part. Kept in sync with <see cref="OrganComponent.Body"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Body;

    /// <summary>
    /// Amount of damage to deal when the part gets removed. Only used when <see cref="IsVital"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 VitalDamage = 100;

    [DataField]
    public string ToolName { get; set; } = "Часть тела"; // Corvax-Localization

    [DataField]
    public string SlotId = string.Empty;

    [DataField, AutoNetworkedField]
    public bool? Used { get; set; } = null;

    [DataField]
    public float Speed { get; set; } = 1f;

    /// <summary>
    /// Max health this body part can have.
    /// </summary>
    [DataField]
    public float MinIntegrity;

    /// <summary>
    /// Whether this body part can be severed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CanSever = true;

    /// <summary>
    /// Whether this body part currently works.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>
    /// Whether this body part can be enabled at all. Used for non-functional prosthetics.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool CanEnable = true;

    /// <summary>
    /// Whether this body part can have children attached.
    /// </summary>
    [DataField]
    public bool CanAttachChildren = true;

    /// <summary>
    /// How long until the part heals itself again.
    /// </summary>
    [DataField]
    public float HealingTime = 30;

    public float HealingTimer;

    /// <summary>
    /// How much integrity the part regains per self-heal.
    /// </summary>
    [DataField]
    public float SelfHealingAmount = 5;

    /// <summary>
    /// The name of the container for this body part. Used in insertion surgeries.
    /// </summary>
    [DataField]
    public string ContainerName { get; set; } = "part_slot";

    /// <summary>
    /// The slot for item insertion.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ItemSlot ItemInsertionSlot = new();

    /// <summary>
    /// Current species. Dictates things like body part sprites.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Species { get; set; } = "";

    /// <summary>
    /// Total damage that has to be dealt to a body part before it can be severed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SeverIntegrity = 90;

    /// <summary>
    /// The ID of the base layer for this body part.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? BaseLayerId;

    /// <summary>
    /// At what <see cref="TargetIntegrity"/> the part is re-enabled.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TargetIntegrity EnableIntegrity = TargetIntegrity.ModeratelyWounded;

    [DataField, AutoNetworkedField]
    public Dictionary<TargetIntegrity, float> IntegrityThresholds = new()
    {
        { TargetIntegrity.CriticallyWounded, 90 },
        { TargetIntegrity.HeavilyWounded, 75 },
        { TargetIntegrity.ModeratelyWounded, 60 },
        { TargetIntegrity.SomewhatWounded, 40 },
        { TargetIntegrity.LightlyWounded, 20 },
        { TargetIntegrity.Healthy, 10 },
    };

    [DataField, AutoNetworkedField]
    public BodyPartType PartType = BodyPartType.Other;

    /// <summary>
    /// Whether the owning body dies if every part of this type is removed from it.
    /// </summary>
    [DataField("vital"), AutoNetworkedField]
    public bool IsVital;

    [DataField, AutoNetworkedField]
    public BodyPartSymmetry Symmetry = BodyPartSymmetry.None;

    /// <summary>
    /// When attached, the part ensures these components on the body, and deletes them on removal.
    /// </summary>
    [DataField, AlwaysPushInheritance]
    public ComponentRegistry? OnAdd;

    /// <summary>
    /// When removed, the part ensures these components on the body, and deletes them on attach.
    /// </summary>
    [DataField, AlwaysPushInheritance]
    public ComponentRegistry? OnRemove;
}
