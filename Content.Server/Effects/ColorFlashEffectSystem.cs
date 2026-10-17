using Content.Shared.Effects;
using Robust.Shared.Player;

namespace Content.Server.Effects;

public sealed partial class ColorFlashEffectSystem : SharedColorFlashEffectSystem
{
    public override void RaiseEffect(Color color, List<EntityUid> entities, Filter filter, float? animationLength = null) // Forge-Change
    {
        RaiseNetworkEvent(new ColorFlashEffectEvent(color, GetNetEntityList(entities), animationLength), filter);
    }
}
