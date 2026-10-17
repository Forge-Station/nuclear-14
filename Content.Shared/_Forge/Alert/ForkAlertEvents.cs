using Content.Shared.Alert;

namespace Content.Shared._Forge.Alert;

/// <summary>
/// События нажатия на форковые алерты.
/// </summary>
/// <remarks>
/// Апстрим убрал интерфейс <c>IAlertClick</c>: теперь прототип алерта несёт
/// <see cref="AlertPrototype.ClickEvent"/>, а обработчик подписывается на него как на
/// обычное событие. Типы лежат в Shared, потому что прототип алерта читает и клиент.
/// </remarks>
public sealed partial class AcceptOfferAlertEvent : BaseAlertEvent;

/// <inheritdoc cref="AcceptOfferAlertEvent"/>
public sealed partial class CheckHealthAlertEvent : BaseAlertEvent;

/// <inheritdoc cref="AcceptOfferAlertEvent"/>
public sealed partial class ShowMoodEffectsAlertEvent : BaseAlertEvent;
