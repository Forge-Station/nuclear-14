using Robust.Shared.GameObjects;

namespace Content.Server._NC.Chemistry.Components;

/// <summary>
/// Маркер временного повышения порога крита и смерти.
/// Создаётся и заполняется напрямую из <see cref="RaiseCritThreshold"/>.
/// Удаляется автоматически по таймеру через StatusEffectsSystem.
/// </summary>
[RegisterComponent]
public sealed partial class CritThresholdMetabolismComponent : Component
{
    /// <summary>На сколько поднят порог крита.</summary>
    public int CritModifier;

    /// <summary>На сколько поднят порог смерти.</summary>
    public int DeadModifier;

    /// <summary>Время, когда модификатор истекает (не используется в логике, только для отладки).</summary>
    public TimeSpan ModifierTimer;
}