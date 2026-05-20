using Content.Shared._NC.Trade;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.Trade;

public sealed partial class StoreSystemStructuredLoader
{
    private void ApplyCatalogEntryDisplayMetadata(NcStoreListingDef listing)
    {
        if (listing.MatchMode != PrototypeMatchMode.Tag)
            return;

        if (!_prototypes.TryIndex<NcTradeTagPrototype>(listing.ProductEntity, out var tagTarget))
            return;

        listing.DisplayName = tagTarget.Name;
        listing.Description = tagTarget.Description;
    }

    private bool ValidateStructuredPresetCurrency(
        StorePresetStructuredPrototype preset,
        StoreMode mode,
        ProtoId<StorePresetStructuredPrototype> presetId)
    {
        if (string.IsNullOrWhiteSpace(preset.Currency))
        {
            Sawmill.Warning($"[NcStore] {mode} preset '{presetId}' has empty currency and was skipped.");
            return false;
        }

        if (_prototypes.TryIndex<StackPrototype>(preset.Currency, out var stack) &&
            !string.IsNullOrWhiteSpace(stack.Spawn) &&
            _prototypes.HasIndex<EntityPrototype>(stack.Spawn))
            return true;

        Sawmill.Warning(
            $"[NcStore] {mode} preset '{presetId}' uses invalid currency '{preset.Currency}'. " +
            "Expected a stack prototype with a valid spawn entity.");
        return false;
    }

    private bool ValidateCatalogEntry(
        StoreCatalogEntry entry,
        StoreMode mode,
        ProtoId<StorePresetStructuredPrototype> presetId,
        string categoryId)
    {
        var ok = true;

        var hasProto = !string.IsNullOrWhiteSpace(entry.Proto);
        var hasTagTarget = !string.IsNullOrWhiteSpace(entry.TagTarget);

        if (entry.MatchMode == PrototypeMatchMode.Tag)
        {
            if (!hasTagTarget || hasProto)
            {
                Sawmill.Warning(
                    $"[NcStore] {mode} tag entry in '{presetId}/{categoryId}' must specify tagTarget and no proto.");
                return false;
            }
        }
        else if (!hasProto || hasTagTarget)
        {
            Sawmill.Warning(
                $"[NcStore] {mode} entry in '{presetId}/{categoryId}' must specify proto and no tagTarget.");
            return false;
        }

        var productId = GetCatalogEntryProductId(entry);

        if (entry.Price <= 0)
        {
            Sawmill.Warning(
                $"[NcStore] {mode} entry '{productId}' in '{presetId}/{categoryId}' has non-positive price={entry.Price}.");
            ok = false;
        }

        if (entry.Amount <= 0)
        {
            Sawmill.Warning(
                $"[NcStore] {mode} entry '{productId}' in '{presetId}/{categoryId}' has non-positive amount={entry.Amount}.");
            ok = false;
        }

        if (entry.Count is { } count && (count == 0 || count < -1))
        {
            Sawmill.Warning(
                $"[NcStore] {mode} entry '{productId}' in '{presetId}/{categoryId}' has invalid count={count}. " +
                "Use -1 or a positive value.");
            ok = false;
        }

        if (entry.MatchMode == PrototypeMatchMode.Matcher)
            return ok && ValidateMatcherEntry(entry, mode, presetId, categoryId);

        if (entry.MatchMode == PrototypeMatchMode.Tag)
            return ok && ValidateTagEntry(entry, mode, presetId, categoryId);

        if (!_prototypes.HasIndex<EntityPrototype>(entry.Proto))
        {
            Sawmill.Warning(
                $"[NcStore] {mode} entry '{entry.Proto}' in '{presetId}/{categoryId}' references missing entity prototype.");
            ok = false;
        }

        return ok;
    }

    private static string GetCatalogEntryProductId(StoreCatalogEntry entry) =>
        entry.MatchMode == PrototypeMatchMode.Tag ? entry.TagTarget : entry.Proto;

    private bool ValidateMatcherEntry(
        StoreCatalogEntry entry,
        StoreMode mode,
        ProtoId<StorePresetStructuredPrototype> presetId,
        string categoryId)
    {
        if (string.IsNullOrWhiteSpace(entry.Proto))
        {
            Sawmill.Warning(
                $"[NcStore] Matcher entry in '{presetId}/{categoryId}' has empty proto and was skipped.");
            return false;
        }

        if (!_prototypes.TryIndex<NcMatcherPrototype>(entry.Proto, out var matcher))
        {
            Sawmill.Warning(
                $"[NcStore] Matcher '{entry.Proto}' not found (preset='{presetId}', category='{categoryId}') and was skipped.");
            return false;
        }

        var hasItems = matcher.Items is { Count: > 0 };
        if (!hasItems)
        {
            Sawmill.Warning(
                $"[NcStore] Matcher '{entry.Proto}' has no items and was skipped.");
            return false;
        }

        return true;
    }

    private bool ValidateTagEntry(
        StoreCatalogEntry entry,
        StoreMode mode,
        ProtoId<StorePresetStructuredPrototype> presetId,
        string categoryId)
    {
        if (mode == StoreMode.Buy)
        {
            Sawmill.Warning(
                $"[NcStore] Tag target '{entry.TagTarget}' is used in a buy listing, cannot spawn and was skipped " +
                $"(preset='{presetId}', category='{categoryId}').");
            return false;
        }

        if (ValidateTradeTagTarget(presetId.ToString(), $"category '{categoryId}'", entry.TagTarget))
            return true;

        return false;
    }

    private bool ValidateTradeTagTarget(string ownerId, string path, string tagTargetId)
    {
        if (!_prototypes.TryIndex<NcTradeTagPrototype>(tagTargetId, out var tagTarget))
        {
            Sawmill.Warning($"[NcStore] '{ownerId}' {path} references missing ncTradeTag '{tagTargetId}'.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(tagTarget.Tag) || !_prototypes.HasIndex<TagPrototype>(tagTarget.Tag))
        {
            Sawmill.Warning(
                $"[NcStore] '{ownerId}' {path} ncTradeTag '{tagTargetId}' references missing raw tag '{tagTarget.Tag}'.");
            return false;
        }

        return true;
    }
}
