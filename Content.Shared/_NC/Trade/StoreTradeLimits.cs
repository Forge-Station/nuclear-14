namespace Content.Shared._NC.Trade;


public static class StoreTradeLimits
{
    public const float StoreUseDistance = 2.5f;
    public const float StoreUiCloseDistance = StoreUseDistance;

    public const int MaxStoreMessageIdLength = 96;
    public const int MaxVisibleListingIds = 256;

    public static bool IsValidMessageId(string? id) =>
        !string.IsNullOrWhiteSpace(id) &&
        id.Length <= MaxStoreMessageIdLength;

    public static string ToLogSafeId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return string.Empty;

        return id.Length <= MaxStoreMessageIdLength
            ? id
            : id[..MaxStoreMessageIdLength] + "...";
    }
}
