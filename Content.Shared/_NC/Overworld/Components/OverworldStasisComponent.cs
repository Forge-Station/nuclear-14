namespace Content.Shared.Overworld.Components;

/// <summary>
/// Вешается на основное тело когда игрок в Overworld.
/// OverworldStasisSystem читает этот компонент и блокирует действия.
/// </summary>
[RegisterComponent]
public sealed partial class OverworldStasisComponent : Component
{
    /// <summary>Токен, которым сейчас управляет игрок.</summary>
    [DataField]
    public EntityUid ActiveToken = EntityUid.Invalid;

    /// <summary>Время входа в Overworld (для диагностики).</summary>
    [DataField]
    public TimeSpan EnteredAt;

    // --- HOOK POINT ---
    // CanBeAttackedWhileInStasis, StasisVulnerabilities, etc.
}
