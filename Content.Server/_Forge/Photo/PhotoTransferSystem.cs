using System.Collections.Generic;
using Content.Shared._Forge.Photo;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Photo;

/// <summary>
/// Handles photo-card UI state and secure image transfer from server to clients.
/// </summary>
public sealed class PhotoTransferSystem : EntitySystem
{
    [Dependency] private readonly PhotoBlobStoreSystem _photoBlobStore = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly UserInterfaceSystem _userInterface = default!;

    private const int MaxImageRequestsPerWindow = 5;
    private const float RateLimitWindowSec = 2f;

    private readonly Dictionary<NetUserId, RateLimitEntry> _requestRateLimits = new();

    private sealed class RateLimitEntry
    {
        public TimeSpan WindowStart;
        public int Count;
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotoCardComponent, AfterActivatableUIOpenEvent>(OnOpenCardInterface);
        SubscribeNetworkEvent<PhotoImageRequestEvent>(OnImageDataRequested);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        _requestRateLimits.Clear();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _requestRateLimits.Remove(args.Session.UserId);
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

    private void OnOpenCardInterface(EntityUid uid, PhotoCardComponent component, AfterActivatableUIOpenEvent args)
    {
        var state = new PhotoCardUiState(component.ImageId);
        _userInterface.SetUiState(uid, PhotoCardUiKey.Key, state);
    }

    private void OnImageDataRequested(PhotoImageRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CheckRateLimit(args.SenderSession))
            return;

        var data = _photoBlobStore.GetBlobData(ev.ImageId);
        if (data == null)
            return;

        if (!_photoBlobStore.TryGetBlobCards(ev.ImageId, out var cards))
            return;

        if (args.SenderSession.AttachedEntity is not { } actor)
            return;

        var hasAccess = false;
        foreach (var cardUid in cards)
        {
            if (_userInterface.IsUiOpen(cardUid, PhotoCardUiKey.Key, actor))
            {
                hasAccess = true;
                break;
            }
        }

        if (!hasAccess)
            return;

        RaiseNetworkEvent(new PhotoImageDataEvent(ev.ImageId, data), args.SenderSession.Channel);
    }
}
