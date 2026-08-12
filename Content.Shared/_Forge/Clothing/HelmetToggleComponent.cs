using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._N14.Clothing;

/// <summary>
/// Добавьте на головной убор, чтобы при надевании появилась кнопка переключения
/// между двумя equipped-HELMET стейтами.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HelmetToggleComponent : Component
{
    /// <summary>
    /// Прототип action-кнопки.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId ToggleAction = "ActionToggleHelmet";

    [DataField, AutoNetworkedField]
    public EntityUid? ToggleActionEntity;

    /// <summary>
    /// Префикс, который добавляется к equipped-HELMET когда шлем переключён.
    /// Например "alt" → ищется стейт "alt-equipped-HELMET".
    /// </summary>
    [DataField, AutoNetworkedField]
    public string AltPrefix = "alt";

    [DataField, AutoNetworkedField]
    public bool IsToggled;
}
