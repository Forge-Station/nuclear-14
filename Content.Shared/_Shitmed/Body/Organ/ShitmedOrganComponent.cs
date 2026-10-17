using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Body.Organ;

/// <summary>
/// Shitmed's organ data, carried alongside upstream's <see cref="Content.Shared.Body.OrganComponent"/>.
///
/// Fork-owned. Upstream's Nubody rewrite replaced the old organ slot model with a flat
/// <c>body_organs</c> container plus a parent/child graph, and its own OrganComponent only
/// holds the body backref and a category. The fields below are Shitmed's: the component
/// registries applied when the organ is attached or removed, whether the organ currently
/// works, and the surgery tool behaviour.
///
/// Renamed from OrganComponent: upstream now owns that class name, and Robust derives a
/// component's prototype name from its class name, so keeping both would collide.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShitmedOrganComponent : Component, ISurgeryToolComponent
{
    /// <summary>
    /// The body containing this organ. Kept in sync with <see cref="Content.Shared.Body.OrganComponent.Body"/>.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Body;

    /// <summary>
    /// The body this organ was originally part of.
    /// </summary>
    [DataField]
    public EntityUid? OriginalBody;

    [DataField]
    public string SlotId = string.Empty;

    [DataField]
    public string ToolName { get; set; } = "Орган"; // Corvax-Localization

    [DataField]
    public float Speed { get; set; } = 1f;

    [DataField, AutoNetworkedField]
    public bool? Used { get; set; }

    /// <summary>
    /// Components ensured on the body while this organ is attached.
    /// </summary>
    [DataField]
    public ComponentRegistry? OnAdd;

    /// <summary>
    /// Components ensured on the body once this organ is removed.
    /// </summary>
    [DataField]
    public ComponentRegistry? OnRemove;

    /// <summary>
    /// Whether this organ currently works.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>
    /// Whether this organ can be enabled at all.
    /// </summary>
    [DataField]
    public bool CanEnable = true;
}
