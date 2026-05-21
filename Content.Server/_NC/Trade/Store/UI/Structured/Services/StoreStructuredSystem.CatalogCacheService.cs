using Content.Shared._NC.Trade;


namespace Content.Server._NC.Trade;


public sealed partial class StoreStructuredSystem
{
    private readonly IStoreCatalogCache _catalogCache = new StoreCatalogCacheService();

    private interface IStoreCatalogCache
    {
        void Clear(EntityUid store);
        bool TryGet(EntityUid store, int revision, out List<StoreListingStaticData> list);
        void Set(EntityUid store, int revision, List<StoreListingStaticData> list);
    }

    private sealed class StoreCatalogCacheService : IStoreCatalogCache
    {
        private readonly Dictionary<EntityUid, (int Revision, List<StoreListingStaticData> List)> _entries = new();

        public void Clear(EntityUid store) => _entries.Remove(store);

        public bool TryGet(EntityUid store, int revision, out List<StoreListingStaticData> list)
        {
            if (_entries.TryGetValue(store, out var cached) && cached.Revision == revision)
            {
                list = cached.List;
                return true;
            }

            list = default!;
            return false;
        }

        public void Set(EntityUid store, int revision, List<StoreListingStaticData> list) =>
            _entries[store] = (revision, list);
    }
}
