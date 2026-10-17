namespace Content.Server.Stunnable.Components;

[RegisterComponent]
public sealed partial class KnockdownOnHitComponent : Component
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Выбивать ли предметы из рук. Раньше это был форковый DropHeldItemsBehavior,
    /// апстримный TryKnockdown принимает bool.
    /// </summary>
    [DataField]
    public bool DropHeldItems;

    [DataField]
    public bool RefreshDuration = true;
}
