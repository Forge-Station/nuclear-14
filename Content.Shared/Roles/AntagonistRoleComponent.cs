using JetBrains.Annotations;

namespace Content.Shared.Roles;

public abstract partial class AntagonistRoleComponent : Component
{
    [DataField("prototype", required: true)]
    public string? PrototypeId;
}

/// <summary>
/// Mark the antagonist role component as being exclusive
/// IE by default other antagonists should refuse to select the same entity for a different antag role
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
[BaseTypeRequired(typeof(AntagonistRoleComponent))]
public sealed partial class ExclusiveAntagonistAttribute : Attribute
{
}
