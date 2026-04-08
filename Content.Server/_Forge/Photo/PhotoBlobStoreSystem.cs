using System.Collections.Generic;

namespace Content.Server._Forge.Photo;

/// <summary>
/// Round-scoped photo blob storage.
/// Keeps image bytes in RAM and tracks which cards reference each blob.
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
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _blobs.Clear();
        _blobToCards.Clear();
        _storedBytes = 0;
        _nextBlobId = 0;
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

        blobId = ++_nextBlobId;
        _blobs[blobId] = data;
        _storedBytes += data.Length;

        _blobToCards[blobId] = new HashSet<EntityUid> { card };
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
        // Round-only storage: stale IDs from old serialized states are invalid here.
        if (component.ImageId > 0 && !_blobs.ContainsKey(component.ImageId))
            component.ImageId = -1;
    }

    private void OnCardRemoved(EntityUid uid, PhotoCardComponent component, ComponentRemove args)
    {
        DetachCard(component.ImageId, uid);
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

    private void RemoveBlob(int blobId)
    {
        if (!_blobs.Remove(blobId, out var data))
            return;

        _storedBytes = Math.Max(0, _storedBytes - data.Length);
    }
}
