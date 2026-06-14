namespace Content.Shared._Forge.RemoveComponents;

/// <summary>
/// Снимает с сущности перечисленные компоненты при старте. Нужно, когда требуется убрать
/// УНАСЛЕДОВАННЫЙ компонент, а удалить его через наследование прототипов в SS14 нельзя.
/// </summary>
[RegisterComponent]
public sealed partial class RemoveComponentsComponent : Component
{
    /// <summary>
    /// Имена компонентов на снятие (как в YAML, без суффикса Component). Неизвестные имена игнорируются.
    /// </summary>
    [DataField]
    public HashSet<string> Components = new();
}
