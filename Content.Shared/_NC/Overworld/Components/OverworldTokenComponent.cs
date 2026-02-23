using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Overworld.Components;

/// <summary>
/// Токен, которым игрок управляет на Overworld-гриде.
/// Живёт только пока игрок в режиме Overworld.
/// </summary>
[RegisterComponent]
public sealed partial class OverworldTokenComponent : Component
{
    /// <summary>Основное тело игрока — оно в стазисе.</summary>
    [DataField]
    public EntityUid OriginalBody = EntityUid.Invalid;

    /// <summary>
    /// Защита от двойного вызова ExitOverworld через ComponentShutdown.
    /// </summary>
    [DataField]
    public bool IsExiting = false;

    /// <summary>
    /// Entity action "Exit Overworld", выданный через ActionsSystem.
    /// Хранится чтобы удалить при выходе если нужно.
    /// </summary>
    [DataField]
    public EntityUid? ExitActionEntity = null;

    // --- HOOK POINT для будущих энкаунтеров ---
    // IsInEncounter, EncounterEntityUid, StealthRating, etc.
}
