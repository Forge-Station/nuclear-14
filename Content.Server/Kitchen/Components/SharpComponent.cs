namespace Content.Server.Kitchen.Components;

/// <summary>
///     Помечает предмет как острый.
/// </summary>
/// <remarks>
///     Forge-Change: апстрим убрал этот компонент - разделка туш у него теперь идёт по
///     качеству инструмента (Slicing). Но 16 форковых прототипов его объявляют, а
///     форковая «подручная хирургия» делает любой острый предмет скальпелем, поэтому
///     компонент остаётся, уже только как метка остроты: на разделку он не влияет.
/// </remarks>
[RegisterComponent]
public sealed partial class SharpComponent : Component
{
    /// <summary>
    ///     Был ли у предмета <c>ScalpelComponent</c> до того, как его пометили острым.
    /// </summary>
    [DataField]
    public bool HadScalpel;

    /// <summary>
    ///     Был ли у предмета <c>BoneSawComponent</c> до того, как его пометили острым.
    /// </summary>
    [DataField]
    public bool HadBoneSaw;
}
