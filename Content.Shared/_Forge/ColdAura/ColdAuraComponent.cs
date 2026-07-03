using Robust.Shared.GameStates;

namespace Content.Shared._Forge.ColdAura;

[RegisterComponent]
public sealed partial class ColdAuraComponent : Component
{
    [DataField("enabled")]
    public bool Enabled = true;

    [DataField("range")]
    public float Range = 3.0f;

    // Δтемпературы (К/сек). Отрицательное — охлаждает.
    [DataField("temperatureChangePerSecond")]
    public float TemperatureChangePerSecond = -10000f;

    // Не охлаждать ниже этого порога (как в Fresium)
    [DataField("minTemperature")]
    public float MinTemperature = 160.15f;

    [DataField("updateInterval")]
    public float UpdateInterval = 0.5f;

    // Фильтр игроков убран, чтобы не тянуть ActorComponent из другого неймспейса.
    // Если понадобится вернём с корректным типом.
    [DataField("applySlow")]
    public bool ApplySlow = true;

    [DataField("walkSpeedModifier")]
    public float WalkSpeedModifier = 0.6f;

    [DataField("sprintSpeedModifier")]
    public float SprintSpeedModifier = 0.6f;

    // Сколько секунд держать замедление после выхода из радиуса
    [DataField("slowLinger")]
    public float SlowLinger = 0.75f;

    [ViewVariables]
    public float Accumulator = 0f;
}
