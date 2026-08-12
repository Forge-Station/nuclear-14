using Content.Shared._Forge.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;


namespace Content.Client._Forge.Weapons.Ranged.Systems;


/// <summary>
/// Клиентская предикция для блокировки выстрела при заклинивании/поломке.
/// Нужна, чтобы не показывать "фейковый" выстрел (звук/вспышку), когда сервер уже отменит попытку.
/// </summary>
public sealed class GunConditionPredictionSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<GunConditionComponent, AttemptShootEvent>(OnAttemptShoot);
    }

    private void OnAttemptShoot(Entity<GunConditionComponent> ent, ref AttemptShootEvent args)
    {
        if (ent.Comp.Condition <= ent.Comp.BrokenThreshold)
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("gun-condition-shot-blocked-broken");
            return;
        }

        if (!ent.Comp.Jammed)
            return;

        args.Cancelled = true;
        args.Message = Loc.GetString("gun-condition-shot-blocked-jammed");
    }
}
