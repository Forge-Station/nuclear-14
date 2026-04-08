using Content.Server.Hands.Systems;
using Content.Server.Materials;
using Content.Server.Popups;
using Content.Shared._Forge.Photo;
using Content.Shared.Materials;
using Content.Shared.Timing;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;

namespace Content.Server._Forge.Photo;

public sealed class PhotoSystem : SharedPhotoSystem
{
    private const int MaxSize = 1024 * 512;

    /// <summary>
    /// Max allowed PNG dimensions (width or height).
    /// </summary>
    private const int MaxImageDimension = 1024;

    /// <summary>
    /// Max distance (tiles) the reported photo position can be from the camera entity.
    /// </summary>
    private const float MaxPhotoPositionOffset = 15f;

    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly UseDelaySystem _delay = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly MaterialStorageSystem _material = default!;
    [Dependency] private readonly PhotoBlobStoreSystem _photoBlobStore = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Camera
        SubscribeLocalEvent<PhotoCameraComponent, AfterActivatableUIOpenEvent>(OnOpenCameraInterface);
        Subs.BuiEvents<PhotoCameraComponent>(
            PhotoCameraUiKey.Key,
            subs =>
            {
                subs.Event<BoundUIClosedEvent>(OnCameraBoundUiClose);
                subs.Event<PhotoCameraTakeImageMessage>(OnTakeImageMessage);
            });
        SubscribeLocalEvent<PhotoCameraComponent, MaterialAmountChangedEvent>(OnPaperInserted);
    }

    #region Camera

    private void OnOpenCameraInterface(EntityUid uid, PhotoCameraComponent component, AfterActivatableUIOpenEvent args)
    {
        UpdateCameraInterface(uid, component);

        if (component.User != null && component.User.Value != args.User)
        {
            RemCompDeferred<PhotoCameraUserComponent>(component.User.Value);
        }

        component.User = args.User;
        EnsureComp<PhotoCameraUserComponent>(args.User);
    }

    private void OnCameraBoundUiClose(EntityUid uid, PhotoCameraComponent component, BoundUIClosedEvent args)
    {
        if (HasComp<PhotoCameraUserComponent>(args.Actor))
            RemComp<PhotoCameraUserComponent>(args.Actor);

        if (component.User == args.Actor)
            component.User = null;
    }

    private void OnTakeImageMessage(EntityUid uid, PhotoCameraComponent component, PhotoCameraTakeImageMessage message)
    {
        // Validate that the sender is the current camera user.
        if (component.User == null || component.User.Value != message.Actor)
            return;

        if (message.Data.Length > MaxSize)
        {
            _audio.PlayPvs(component.ErrorSound, uid);
            _popup.PopupEntity(Loc.GetString("photo-camera-image-too-large"), uid, message.Actor);
            return;
        }

        if (!PngUtility.ValidatePng(message.Data, MaxImageDimension, MaxImageDimension))
            return;

        // Validate that the reported photo position is within reasonable range of the camera.
        var cameraPos = _transform.GetMapCoordinates(uid);
        if (cameraPos.MapId != message.PhotoPosition.MapId)
            return;

        var offset = (message.PhotoPosition.Position - cameraPos.Position).Length();
        if (offset > MaxPhotoPositionOffset)
            return;

        // Validate zoom is within component bounds.
        var zoom = Math.Clamp(message.Zoom, component.MinZoom, component.MaxZoom);

        if (TryTakeImage(uid, component, message.Data))
            RaiseLocalEvent(new PhotoCameraTakeImageEvent(uid, message.Actor, message.PhotoPosition, zoom));
    }

    private void UpdateCameraInterface(EntityUid uid, PhotoCameraComponent component)
    {
        var hasPaper = _material.CanChangeMaterialAmount(uid, component.CardMaterial, -component.CardCost);
        var state = new PhotoCameraUiState(GetNetEntity(uid), hasPaper);
        _userInterface.SetUiState(uid, PhotoCameraUiKey.Key, state);
    }

    private void OnPaperInserted(EntityUid uid, PhotoCameraComponent component, MaterialAmountChangedEvent args)
    {
        if (TryComp<MaterialStorageComponent>(uid, out var storage))
            Dirty(uid, storage);

        UpdateCameraInterface(uid, component);
    }

    private bool TryTakeImage(EntityUid uid, PhotoCameraComponent component, byte[] imageData)
    {
        if (!TryComp(uid, out UseDelayComponent? useDelay))
            return false;

        if (_delay.IsDelayed((uid, useDelay)))
            return false;

        _delay.TryResetDelay((uid, useDelay));

        var printCard = PrintCard(uid, component, imageData);

        if (printCard)
            _audio.PlayPvs(component.PhotoSound, uid);
        else
            _audio.PlayPvs(component.ErrorSound, uid);

        return printCard;
    }

    private bool PrintCard(EntityUid uid, PhotoCameraComponent component, byte[] imageData)
    {
        if (!_photoBlobStore.HasCapacityFor(imageData.Length))
        {
            if (component.User != null)
                _popup.PopupEntity(Loc.GetString("photo-camera-memory-full"), uid, component.User.Value);

            return false;
        }

        if (!_material.TryChangeMaterialAmount(uid, component.CardMaterial, -component.CardCost))
        {
            if (component.User != null)
                _popup.PopupEntity(Loc.GetString("photo-camera-no-paper"), uid, component.User.Value);

            return false;
        }

        var card = Spawn(component.CardPrototype);
        _transform.SetMapCoordinates(card, _transform.GetMapCoordinates(uid));

        if (!TryComp(card, out PhotoCardComponent? photo))
        {
            _material.TryChangeMaterialAmount(uid, component.CardMaterial, component.CardCost);
            QueueDel(card);
            UpdateCameraInterface(uid, component);
            return false;
        }

        if (!_photoBlobStore.TryStoreForCard(card, imageData, out var imageId))
        {
            _material.TryChangeMaterialAmount(uid, component.CardMaterial, component.CardCost);
            QueueDel(card);

            if (component.User != null)
                _popup.PopupEntity(Loc.GetString("photo-camera-memory-full"), uid, component.User.Value);

            UpdateCameraInterface(uid, component);
            return false;
        }

        photo.ImageId = imageId;

        if (component.User != null)
            _hands.TryPickupAnyHand(component.User.Value, card);

        UpdateCameraInterface(uid, component);
        return true;
    }

    #endregion
}

