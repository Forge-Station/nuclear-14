using Content.Shared._NC.Trade;
using Robust.Shared.Audio;

namespace Content.Server._NC.Trade;

public sealed partial class StoreStructuredSystem : EntitySystem
{
    private void OnClaimContract(EntityUid uid, NcStoreComponent comp, ClaimContractBoundMessage msg)
    {
        if (!TryGetLockedUiUser(uid, comp, out var user))
            return;

        if (!_storeSystem.CanUseStore(uid, comp, user))
            return;

        if (TryComp(uid, out TransformComponent? sX) && TryComp(user, out TransformComponent? uX) &&
            !_xform.InRange(sX.Coordinates, uX.Coordinates, AutoCloseDistance))
            return;

        if (_contracts.TryClaim(uid, user, msg.ContractId))
        {
            _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg"), user);
            _popups.PopupEntity(Loc.GetString("nc-store-contract-completed"), uid, user);
            UpdateDynamicState(uid, comp, user);
            return;
        }

        EntityUid? crateUid = null;
        if (_logic.TryGetPulledClosedCrate(user, out var pulledCrate))
        {
            crateUid = pulledCrate;
            _inventory.ScanInventoryItems(pulledCrate, _deepCrateItemsScratch);
        }

        _inventory.ScanInventoryItems(user, _deepUserItemsScratch);

        _contracts.UpdateContractsProgress(
            comp,
            user,
            _deepUserItemsScratch,
            crateUid,
            crateUid != null ? _deepCrateItemsScratch : null);

        UpdateDynamicState(uid, comp, user);
    }
}
