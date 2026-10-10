namespace Content.Client._Forge.Newspapers;

/// <summary>Retry a missing image; opening from cached BUI state may precede server acknowledgement.</summary>
internal sealed class NewspaperPhotoRequest
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);
    private TimeSpan _nextAttempt;
    private int _edition;
    private int _attempts;
    private const int MaxAttempts = 5;
    public int PhotoId { get; private set; } = -1;

    public void SetTarget(int photoId, int edition)
    {
        if (PhotoId != photoId || _edition != edition)
        {
            _nextAttempt = TimeSpan.Zero; _attempts = 0;
        }
        PhotoId = photoId;
        _edition = edition;
    }

    public bool TryRequest(TimeSpan now, bool received, out int edition)
    {
        edition = _edition;
        if (PhotoId < 0 || received || _attempts >= MaxAttempts || now < _nextAttempt)
            return false;
        _attempts++;
        _nextAttempt = now + RetryInterval;
        return true;
    }
}
