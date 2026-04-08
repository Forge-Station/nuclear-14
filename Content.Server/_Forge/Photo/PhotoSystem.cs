using System.Collections.Generic;
using Content.Server.Hands.Systems;
using Content.Server.Materials;
using Content.Server.Popups;
using Content.Shared._Forge.Photo;
using Content.Shared.Materials;
using Content.Shared.Timing;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

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
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    #region Image Storage

    /// <summary>
    /// Runtime image store. Maps ImageId -> raw PNG bytes.
    /// Entries are removed when their PhotoCardComponent is deleted.
    /// </summary>
    private readonly Dictionary<int, byte[]> _imageStore = new();

    /// <summary>
    /// Reverse index: ImageId -> set of card EntityUids that use this image.
    /// Allows O(1) access check instead of iterating all PhotoCardComponents.
    /// </summary>
    private readonly Dictionary<int, HashSet<EntityUid>> _imageIndex = new();

    private int _nextImageId;

    private int RegisterImage(byte[] data, EntityUid card)
    {
        var id = ++_nextImageId;
        _imageStore[id] = data;

        if (!_imageIndex.TryGetValue(id, out var set))
        {
            set = new HashSet<EntityUid>();
            _imageIndex[id] = set;
        }

        set.Add(card);
        return id;
    }

    private void UnregisterImage(int imageId, EntityUid card)
    {
        if (imageId <= 0)
            return;

        if (_imageIndex.TryGetValue(imageId, out var set))
        {
            set.Remove(card);

            if (set.Count == 0)
            {
                _imageIndex.Remove(imageId);
                _imageStore.Remove(imageId);
            }
        }
        else
        {
            _imageStore.Remove(imageId);
        }
    }

    public byte[]? GetImageData(int imageId)
    {
        return _imageStore.GetValueOrDefault(imageId);
    }

    #endregion

    #region Rate Limiting

    /// <summary>
    /// Per-session rate limit for image data requests.
    /// </summary>
    private const int MaxImageRequestsPerWindow = 5;
    private const float RateLimitWindowSec = 2f;

    private readonly Dictionary<NetUserId, RateLimitEntry> _requestRateLimits = new();

    private sealed class RateLimitEntry
    {
        public TimeSpan WindowStart;
        public int Count;
    }

    private bool CheckRateLimit(ICommonSession session)
    {
        var now = _timing.CurTime;

        if (!_requestRateLimits.TryGetValue(session.UserId, out var entry))
        {
            entry = new RateLimitEntry { WindowStart = now, Count = 0 };
            _requestRateLimits[session.UserId] = entry;
        }

        if ((now - entry.WindowStart).TotalSeconds > RateLimitWindowSec)
        {
            entry.WindowStart = now;
            entry.Count = 0;
        }

        entry.Count++;
        return entry.Count <= MaxImageRequestsPerWindow;
    }

    #endregion

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

        // Card
        SubscribeLocalEvent<PhotoCardComponent, AfterActivatableUIOpenEvent>(OnOpenCardInterface);
        SubscribeLocalEvent<PhotoCardComponent, MapInitEvent>(OnCardMapInit);
        SubscribeLocalEvent<PhotoCardComponent, ComponentRemove>(OnCardRemoved);
        SubscribeNetworkEvent<PhotoImageRequestEvent>(OnImageDataRequested);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        _imageStore.Clear();
        _imageIndex.Clear();
        _requestRateLimits.Clear();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _requestRateLimits.Remove(args.Session.UserId);
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
        if (!_material.TryChangeMaterialAmount(uid, component.CardMaterial, -component.CardCost))
        {
            if (component.User != null)
                _popup.PopupEntity(Loc.GetString("photo-camera-no-paper"), uid, component.User.Value);

            return false;
        }

        var card = Spawn(component.CardPrototype);
        _transform.SetMapCoordinates(card, _transform.GetMapCoordinates(uid));

        if (TryComp<PhotoCardComponent>(card, out var photo))
        {
            photo.ImageData = imageData;
            photo.ImageId = RegisterImage(imageData, card);
        }

        if (component.User != null)
            _hands.TryPickupAnyHand(component.User.Value, card);

        UpdateCameraInterface(uid, component);
        return true;
    }

    #endregion

    #region Card

    private void OnCardMapInit(EntityUid uid, PhotoCardComponent component, MapInitEvent args)
    {
        if (component.ImageData != null && component.ImageId == -1)
        {
            component.ImageId = RegisterImage(component.ImageData, uid);
        }
    }

    /// <summary>
    /// Clean up image data from runtime store when card entity is removed.
    /// Prevents unbounded memory growth over long rounds.
    /// </summary>
    private void OnCardRemoved(EntityUid uid, PhotoCardComponent component, ComponentRemove args)
    {
        UnregisterImage(component.ImageId, uid);
    }

    private void OnOpenCardInterface(EntityUid uid, PhotoCardComponent component, AfterActivatableUIOpenEvent args)
    {
        var state = new PhotoCardUiState(component.ImageId);
        _userInterface.SetUiState(uid, PhotoCardUiKey.Key, state);
    }

    /// <summary>
    /// Client requested image data. Rate-limited and access-checked:
    /// only responds if the requesting player has a card with this ImageId
    /// in their hands or inventory (via open BUI).
    /// Uses _imageIndex for O(1) lookup instead of iterating all cards.
    /// </summary>
    private void OnImageDataRequested(PhotoImageRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckRateLimit(args.SenderSession))
            return;

        var data = GetImageData(ev.ImageId);
        if (data == null)
            return;

        // Access check via index: find cards with this ImageId, verify BUI is open.
        if (!_imageIndex.TryGetValue(ev.ImageId, out var cardUids))
            return;

        if (args.SenderSession.AttachedEntity is not { } actor)
            return;

        var found = false;
        foreach (var cardUid in cardUids)
        {
            if (_userInterface.IsUiOpen(cardUid, PhotoCardUiKey.Key, actor))
            {
                found = true;
                break;
            }
        }

        if (!found)
            return;

        RaiseNetworkEvent(new PhotoImageDataEvent(ev.ImageId, data), args.SenderSession.Channel);
    }

    #endregion
}

