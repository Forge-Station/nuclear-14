namespace Content.Shared._Forge.RemoveComponents;

// Убирает перечисленные компоненты при спавне. Нужно потому, что компонент, доставшийся от родителя,
// в SS14 через YAML обратно не выкинуть — приходится снимать кодом.
[RegisterComponent]
public sealed partial class RemoveComponentsComponent : Component
{
    // Имена компонентов как в YAML, без суффикса Component. Если такого компонента нет — просто пропустим.
    [DataField]
    public HashSet<string> Components = new();
}
