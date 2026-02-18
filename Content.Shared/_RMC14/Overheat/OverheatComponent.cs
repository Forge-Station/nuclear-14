using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;


namespace Content.Shared.WeaponMounts.Overheat;


/// <summary>
///     Добавляет оружию механику перегрева.
///     При достижении <see cref="MaxHeat" /> стрельба блокируется
///     и наносится урон оружию или его станку.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState,]
public sealed partial class OverheatComponent : Component
{
    /// <summary>Охлаждение в секунду при нормальной работе.</summary>
    [DataField, AutoNetworkedField,]
    public float CooldownRate = 2;

    /// <summary>Урон, наносимый оружию (или его станку) при перегреве.</summary>
    [DataField, AutoNetworkedField,]
    public DamageSpecifier Damage = new()
    {
        DamageDict = { ["Heat"] = 30, }
    };

    /// <summary>Время блокировки стрельбы после перегрева.</summary>
    [DataField, AutoNetworkedField,]
    public TimeSpan EmergencyCooldownDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Коэффициент аварийного сброса тепла при перегреве.
    ///     Текущий нагрев умножается на это значение (должно быть меньше 1).
    /// </summary>
    [DataField, AutoNetworkedField,]
    public float EmergencyCooldownMultiplier = 0.375f;

    /// <summary>Текущий уровень нагрева.</summary>
    [DataField, AutoNetworkedField,]
    public float Heat;

    /// <summary>Нагрев за один выстрел.</summary>
    [DataField, AutoNetworkedField,]
    public float HeatPerShot = 1;

    /// <summary>Порог перегрева.</summary>
    [DataField, AutoNetworkedField,]
    public int MaxHeat = 40;

    /// <summary>Оружие сейчас в состоянии перегрева (стрельба заблокирована).</summary>
    [DataField, AutoNetworkedField,]
    public bool Overheated;

    /// <summary>Время начала перегрева.</summary>
    [DataField, AutoNetworkedField,]
    public TimeSpan OverheatedAt;

    /// <summary>Звук при перегреве.</summary>
    [DataField, AutoNetworkedField,]
    public SoundSpecifier? OverheatSound = new SoundPathSpecifier("/Audio/Effects/sizzle.ogg");
}
