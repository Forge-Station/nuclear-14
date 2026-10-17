using Robust.Shared.Serialization;

namespace Content.Shared.DeltaV.VendingMachines;

/// <summary>
/// Ключ данных внешнего вида торгового автомата.
/// Апстрим убрал общие VendingMachineVisuals: его автоматы считают вид
/// на клиенте по своим компонентам, а слои-перечисление живёт в Content.Client.
/// Форковому автомату нужен общий ключ, поэтому он свой.
/// </summary>
[Serializable, NetSerializable]
public enum ShopVendorVisuals : byte
{
    VisualState,
}

/// <inheritdoc cref="ShopVendorVisuals"/>
[Serializable, NetSerializable]
public enum ShopVendorVisualState : byte
{
    Normal,
    Off,
    Broken,
    Eject,
    Deny,
}
