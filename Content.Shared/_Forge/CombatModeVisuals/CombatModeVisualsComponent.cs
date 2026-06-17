using Robust.Shared.GameStates;

namespace Content.Shared._Forge.CombatModeVisuals;

/// <summary>
/// Оверлей-слои спрайта, видимые только когда сущность в боевом режиме (CombatMode). Вне боя — скрыты.
/// Обобщение «борг с красными глазами»: повесь компонент и перечисли map-ключи слоёв.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CombatModeVisualsComponent : Component
{
    /// <summary>Map-ключи слоёв спрайта, показываемых в боевом режиме.</summary>
    [DataField]
    public List<string> Layers = new();

    /// <summary>Клиентский кэш последнего применённого состояния — чтобы не дёргать спрайт каждый тик.</summary>
    public bool LastInCombat;
}
