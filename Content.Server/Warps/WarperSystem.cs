/// Forge-Change-Start
using Content.Shared.DoAfter;
using Content.Shared._Forge.Warps;
/// Forge-Change-End
using Content.Server.Ghost.Components;
using Content.Server.Popups;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using System.Numerics;

namespace Content.Server.Warps;

public class WarperSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly WarpPointSystem _warpPointSystem = default!;
    [Dependency] private readonly SharedTransformSystem _sharedTransform = default!;
    [Dependency] private readonly PullingSystem _pullingSystem = default!;

    /// Forge-Change
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WarperComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<WarperComponent, WarpDoAfterEvent>(OnWarpFinished); /// Forge-Change
    }

    private void OnInteractHand(EntityUid uid, WarperComponent component, InteractHandEvent args)
    {
        /// Forge-Change-Start
        if (args.Handled)
            return;

        args.Handled = true;
        TryTravel(uid, component, args.User);
    }

    private void OnWarpFinished(EntityUid uid, WarperComponent component, WarpDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        TryTravel(uid, component, args.User, delayCompleted: true);
    }

    /// <summary>
    /// Проверяет место назначения и запускает задержку перехода либо перемещает пользователя.
    /// Вызывается как при взаимодействии с лестницей, так и после завершения DoAfter:
    /// в обоих случаях user — инициатор взаимодействия (args.User), а uid — сама лестница.
    /// После ожидания место назначения и состояние его карты проверяются заново.
    /// При отмене DoAfter этот метод повторно не вызывается.
    /// </summary>
    /// <param name="uid">Лестница или другой объект с WarperComponent, с которым взаимодействовали.</param>
    /// <param name="component">Компонент этого объекта с ID места назначения и длительностью задержки.</param>
    /// <param name="user">Перемещаемый персонаж; также получатель сообщений об ошибках.</param>
    /// <param name="delayCompleted">
    /// True только при завершении DoAfter: пропускает повторный запуск ожидания, но не проверки назначения.
    /// При нулевой задержке или взаимодействии призрака переход выполняется сразу.
    /// </param>
    /// <remarks>
    /// Перенос и обнуление скорости сохраняют прежнюю логику OnInteractHand.
    /// Если в момент перехода user тянет объект, сначала переносится этот объект, затем user,
    /// после чего восстанавливается перетаскивание. Объект, отпущенный во время ожидания, не переносится.
    /// </remarks>
    private void TryTravel(EntityUid uid, WarperComponent component, EntityUid user, bool delayCompleted = false)
    {
        /// Forge-Change-End
        if (string.IsNullOrEmpty(component.ID)) /// Forge-Change
        {
            Logger.DebugS("warper", "Warper has no destination");
            _popupSystem.PopupEntity(Loc.GetString("warper-goes-nowhere", ("warper", uid)), user, Filter.Entities(user), true);
            return;
        }

        var dest = _warpPointSystem.FindWarpPoint(component.ID);
        if (dest is null)
        {
            Logger.DebugS("warper", String.Format("Warp destination '{0}' not found", component.ID));
            _popupSystem.PopupEntity(Loc.GetString("warper-goes-nowhere", ("warper", uid)), user, Filter.Entities(user), true);
            return;
        }

        var entMan = IoCManager.Resolve<IEntityManager>();
        TransformComponent? destXform;
        entMan.TryGetComponent<TransformComponent>(dest.Value, out destXform);
        if (destXform is null)
        {
            Logger.DebugS("warper", String.Format("Warp destination '{0}' has no transform", component.ID));
            _popupSystem.PopupEntity(Loc.GetString("warper-goes-nowhere", ("warper", uid)), user, Filter.Entities(user), true);
            return;
        }

        // Check that the destination map is initialized and return unless in aghost mode.
        var mapMgr = IoCManager.Resolve<IMapManager>();
        var destMap = destXform.MapID;
        if (!mapMgr.IsMapInitialized(destMap) || mapMgr.IsMapPaused(destMap))
        {
            if (!entMan.HasComponent<GhostComponent>(user))
            {
                // Normal ghosts cannot interact, so if we're here this is already an admin ghost.
                Logger.DebugS("warper", String.Format("Player tried to warp to '{0}', which is not on a running map", component.ID));
                _popupSystem.PopupEntity(Loc.GetString("warper-goes-nowhere", ("warper", uid)), user, Filter.Entities(user), true);
                return;
            }
        }

        /// Forge-Change-Start
        if (!delayCompleted && component.TravelDelay > 0 && !HasComp<GhostComponent>(user))
        {
            _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, component.TravelDelay,
                new WarpDoAfterEvent(), uid, target: uid)
            {
                BreakOnMove = true,
                NeedHand = false,
            });
            return;
        }
        /// Forge-Change-End

        // Forge-Change-Start
        if (TryComp(user, out PullerComponent? puller) && puller.Pulling != null)
        {
            var pullerItem = puller.Pulling.Value;
            _sharedTransform.SetCoordinates(pullerItem, destXform.Coordinates);
            _sharedTransform.AttachToGridOrMap(pullerItem);
            _sharedTransform.SetCoordinates(user, destXform.Coordinates);
            _sharedTransform.AttachToGridOrMap(user);
            _pullingSystem.TryStartPull(user, pullerItem); // Срёт ошибкой на клиенте, не критично, но не приятно.
        }

        else
        {
            _sharedTransform.SetCoordinates(user, destXform.Coordinates);
            _sharedTransform.AttachToGridOrMap(user);
        }

        if (HasComp<PhysicsComponent>(user))
        {
            _physics.SetLinearVelocity(user, Vector2.Zero);
        }
        // Forge-Change-End
    }
}
