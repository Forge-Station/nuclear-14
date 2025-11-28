using Content.Shared._NC.Trade;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._NC.Trade;

public sealed class StoreSystemStructuredLoader : EntitySystem
{
    private static readonly ISawmill Sawmill = Logger.GetSawmill("ncstore-loader");
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly NcContractSystem _contracts = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<NcStoreComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NcStoreComponent, ComponentStartup>(OnStartup);
    }

    private void OnMapInit(EntityUid uid, NcStoreComponent comp, MapInitEvent args)
    {
        // грузим пресет магазина
        TryLoadPreset(uid, comp, "MapInit");
        // инициализируем контракты
        _contracts.InitContractsForStore(uid, comp);
    }

    private void OnStartup(EntityUid uid, NcStoreComponent comp, ComponentStartup args)
    {
        if (comp.Listings.Count == 0)
            TryLoadPreset(uid, comp, "ComponentStartup");

        // второй шанс инициализировать контракты (метод идемпотентный)
        _contracts.InitContractsForStore(uid, comp);
    }

    private void TryLoadPreset(EntityUid uid, NcStoreComponent comp, string reason)
    {
        if (string.IsNullOrWhiteSpace(comp.Preset))
        {
            Sawmill.Warning($"[NcStore] Нет пресета у {ToPrettyString(uid)} ({reason})");
            return;
        }

        if (!_prototypes.TryIndex<StorePresetStructuredPrototype>(comp.Preset, out var preset))
        {
            Sawmill.Error($"[NcStore] Пресет '{comp.Preset}' не найден для {ToPrettyString(uid)} ({reason})");
            return;
        }

        comp.CurrencyWhitelist.Clear();
        comp.Categories.Clear();
        comp.Listings.Clear();

        comp.CurrencyWhitelist.Add(preset.Currency);

        var count = 0;
        foreach (var (modeStr, categories) in preset.Catalog)
        {
            var mode = modeStr switch
            {
                "Buy" => StoreMode.Buy,
                "Sell" => StoreMode.Sell,
                _ => StoreMode.Buy
            };

            foreach (var (category, entries) in categories)
            {
                if (!comp.Categories.Contains(category))
                    comp.Categories.Add(category);

                foreach (var entry in entries)
                {
                    var id = $"{mode}_{category}_{entry.Proto}_{_random.Next(100000)}";

                    comp.Listings.Add(
                        new()
                        {
                            Id = id,
                            ProductEntity = entry.Proto,
                            Cost = new() { [preset.Currency] = entry.Price, },
                            Categories = [category],
                            Conditions = new(),
                            Mode = mode,
                            RemainingCount = entry.Count ?? -1
                        });

                    count++;
                }
            }
        }

        Sawmill.Info(
            $"[NcStore] Загружено {count} товаров для {ToPrettyString(uid)} (preset={comp.Preset}, reason={reason})");
    }
}
