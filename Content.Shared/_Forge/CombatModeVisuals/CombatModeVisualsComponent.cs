using Robust.Shared.GameStates;

namespace Content.Shared._Forge.CombatModeVisuals;

/// <summary>
/// Слои спрайта, которые видно только когда существо в боевом режиме (например, красные глаза у борга).
/// Вне боя они спрятаны. Просто навесь компонент и перечисли нужные слои.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CombatModeVisualsComponent : Component
{
    /// <summary>Имена слоёв спрайта, которые показываем в бою.</summary>
    [DataField]
    public List<string> Layers = new();

    /// <summary>Запоминаем прошлое состояние, чтобы зря не трогать спрайт каждый тик.</summary>
    public bool LastInCombat;
}
