namespace Content.Shared._Forge.RemoveComponents;

// Снимает перечисленные компоненты на старте — обход того, что унаследованный компонент в SS14 не удалить.
[RegisterComponent]
public sealed partial class RemoveComponentsComponent : Component
{
    // Имена компонентов на снятие (как в YAML, без суффикса Component); неизвестные игнорируются.
    [DataField]
    public HashSet<string> Components = new();
}
