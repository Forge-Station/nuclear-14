using Robust.Shared.GameObjects;

namespace Content.Shared._RMC14.Fire;

/// <summary>
/// Компонент-маркер для тайлов огня, которые должны игнорировать
/// иммунитет сущностей к огню (FireImmune = true).
///
/// Используется для особых типов огня:
/// - RMCTileFireOBAegis  — огонь орбитальной бомбардировки Aegis
/// - RMCTileFireNapalmE  — напалм-E (обходит иммунитет ксено)
/// - RMCTileFireNapalmEX — напалм-EX
/// - RMCTileFireR189     — R189
///
/// Логика обхода иммунитета реализована в RMCIgniteOnCollideSystem.
/// </summary>
[RegisterComponent]
public sealed partial class RMCFireImmunityBypassComponent : Component
{
}
