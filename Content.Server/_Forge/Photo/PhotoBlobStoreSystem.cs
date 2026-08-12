using Content.Shared.GameTicking;


namespace Content.Server._Forge.Photo;

/// <summary>
/// Round-scoped photo blob storage.
/// Keeps image bytes in RAM and tracks which cards reference each blob.
/// For persisted cards, blobs are restored from PhotoCardComponent.ImageData on MapInit.
/// </summary>
public sealed class PhotoBlobStoreSystem : EntitySystem
{
    private const long MaxTotalPhotoBytes = 128L * 1024 * 1024;

    private readonly Dictionary<int, byte[]> _blobs = new();
    private readonly Dictionary<int, HashSet<EntityUid>> _blobToCards = new();

    private int _nextBlobId;
    private long _storedBytes;

    public int StoredBlobCount => _blobs.Count;
    public long StoredBlobBytes => _storedBytes;
    public long MaxBlobBytes => MaxTotalPhotoBytes;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotoCardComponent, ComponentRemove>(OnCardRemoved);
        SubscribeLocalEvent<PhotoCardComponent, MapInitEvent>(OnCardMapInit);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ClearAll();
    }

    public bool HasCapacityFor(int imageSizeBytes)
    {
        return _storedBytes + imageSizeBytes <= MaxTotalPhotoBytes;
    }

    public bool TryStoreForCard(EntityUid card, byte[] data, out int blobId)
    {
        blobId = -1;
        if (!HasCapacityFor(data.Length))
            return false;

        blobId = StoreBlobForCard(card, data);
        return true;
    }

    public byte[]? GetBlobData(int blobId)
    {
        return _blobs.GetValueOrDefault(blobId);
    }

    public bool TryGetBlobCards(int blobId, out HashSet<EntityUid> cards)
    {
        return _blobToCards.TryGetValue(blobId, out cards!);
    }

    private void OnCardMapInit(EntityUid uid, PhotoCardComponent component, MapInitEvent args)
    {
        if (component.ImageData == null)
        {
            component.ImageId = -1;
            return;
        }

        // If runtime ID is already valid, just ensure reverse index contains this card.
        if (component.ImageId > 0 && _blobs.ContainsKey(component.ImageId))
        {
            AttachCardReference(component.ImageId, uid);
            return;
        }

        // Restore from persisted bytes. We intentionally bypass capacity checks here:
        // existing map content should remain viewable after load.
        component.ImageId = StoreBlobForCard(uid, component.ImageData);
    }

    private void OnCardRemoved(EntityUid uid, PhotoCardComponent component, ComponentRemove args)
    {
        DetachCard(component.ImageId, uid);
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        ClearAll();
    }

    private void DetachCard(int blobId, EntityUid card)
    {
        if (blobId <= 0)
            return;

        if (_blobToCards.TryGetValue(blobId, out var cards))
        {
            cards.Remove(card);
            if (cards.Count == 0)
            {
                _blobToCards.Remove(blobId);
                RemoveBlob(blobId);
            }

            return;
        }

        RemoveBlob(blobId);
    }

    private int StoreBlobForCard(EntityUid card, byte[] data)
    {
        var blobId = ++_nextBlobId;
        _blobs[blobId] = data;
        _storedBytes += data.Length;
        _blobToCards[blobId] = new HashSet<EntityUid> { card };
        return blobId;
    }

    private void AttachCardReference(int blobId, EntityUid card)
    {
        if (!_blobToCards.TryGetValue(blobId, out var cards))
        {
            cards = new HashSet<EntityUid>();
            _blobToCards[blobId] = cards;
        }

        cards.Add(card);
    }

    private void RemoveBlob(int blobId)
    {
        if (!_blobs.Remove(blobId, out var data))
            return;

        _storedBytes = Math.Max(0, _storedBytes - data.Length);
    }

    private void ClearAll()
    {
        _blobs.Clear();
        _blobToCards.Clear();
        _storedBytes = 0;
        _nextBlobId = 0;
    }
}
