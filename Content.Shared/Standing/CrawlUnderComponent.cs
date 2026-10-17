using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Standing;

/// <summary>
/// Позволяет лежащей сущности ползать под мебелью. Апстримное ползание
/// (<see cref="Content.Shared.Stunnable.CrawlerComponent"/>) этого не умеет:
/// оно даёт само лежачее состояние, время вставания и замедление, а здесь
/// остаётся только форковая надстройка - глубина отрисовки и доп. замедление.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CrawlUnderComponent : Component
{
    /// <summary>
    /// Сущность сама решила ползти под мебелью. На физику не влияет, только на отрисовку.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IsCrawlingUnder;

    /// <summary>
    /// Множитель скорости поверх лежачего, пока ползём под мебелью.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float SpeedModifier = 0.5f;

    /// <remarks>
    /// Переключение глубины отрисовки на ходу глючит, поэтому значения держим равными.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public int NormalDrawDepth = (int) DrawDepth.DrawDepth.Mobs,
               CrawlingUnderDrawDepth = (int) DrawDepth.DrawDepth.Mobs;
}

/// <summary>
/// Запрос клиента на переключение ползания под мебелью.
/// </summary>
[Serializable, NetSerializable]
public sealed class ToggleCrawlUnderEvent : EntityEventArgs;
