using Content.Shared.Traits.Assorted;

namespace Content.Shared.Traits.Assorted.Systems;

/// <summary>
/// Трейт «слабая голова»: алкоголь должен действовать сильнее.
/// </summary>
/// <remarks>
/// Forge-Change: не работает. Трейт висел на событии <c>TryMetabolizeReagent</c>,
/// которым метаболизатор спрашивал, с каким масштабом применять конкретный реагент.
/// Апстрим метаболизм переписал, и такого хука у него нет: масштаб приходит в
/// событие эффекта уже посчитанным. Компонент и его множитель оставлены - прототипы
/// трейта на них ссылаются, - но применять множитель пока негде.
/// </remarks>
public sealed class LightweightDrunkSystem : EntitySystem
{
}
