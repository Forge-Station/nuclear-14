using Content.Shared.Audio;
using Content.Shared.CartridgeLoader;
using Content.Shared.DeltaV.CartridgeLoader.Cartridges;
using Content.Shared.DeltaV.NanoChat;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.Interaction;

// Апстрим перенёс LogProbeCartridgeSystem в Content.Shared, так что
// форковая часть про NanoChat переехала следом - partial обязан быть
// в той же сборке.
namespace Content.Shared.CartridgeLoader.Cartridges;

public sealed partial class LogProbeCartridgeSystem
{
    private void InitializeNanoChat()
    {
        SubscribeLocalEvent<NanoChatRecipientUpdatedEvent>(OnRecipientUpdated);
        SubscribeLocalEvent<NanoChatMessageReceivedEvent>(OnMessageReceived);
    }

    private void OnRecipientUpdated(ref NanoChatRecipientUpdatedEvent args)
    {
        var query = EntityQueryEnumerator<LogProbeCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var probe, out var cartridge))
        {
            if (probe.ScannedNanoChatData == null || GetEntity(probe.ScannedNanoChatData.Value.Card) != args.CardUid)
                continue;

            if (!TryComp<NanoChatCardComponent>(args.CardUid, out var card))
                continue;

            probe.ScannedNanoChatData = new NanoChatData(
                new Dictionary<uint, NanoChatRecipient>(card.Recipients),
                probe.ScannedNanoChatData.Value.Messages,
                card.Number,
                GetNetEntity(args.CardUid));

            if (cartridge.LoaderUid != null)
                UpdateUiState((uid, probe), cartridge.LoaderUid.Value);
        }
    }

    private void OnMessageReceived(ref NanoChatMessageReceivedEvent args)
    {
        var query = EntityQueryEnumerator<LogProbeCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var probe, out var cartridge))
        {
            if (probe.ScannedNanoChatData == null || GetEntity(probe.ScannedNanoChatData.Value.Card) != args.CardUid)
                continue;

            if (!TryComp<NanoChatCardComponent>(args.CardUid, out var card))
                continue;

            probe.ScannedNanoChatData = new NanoChatData(
                probe.ScannedNanoChatData.Value.Recipients,
                new Dictionary<uint, List<NanoChatMessage>>(card.Messages),
                card.Number,
                GetNetEntity(args.CardUid));

            if (cartridge.LoaderUid != null)
                UpdateUiState((uid, probe), cartridge.LoaderUid.Value);
        }
    }

    private void ScanNanoChatCard(Entity<LogProbeCartridgeComponent> ent,
        ref CartridgeRelayedEvent<AfterInteractEvent> args,
        EntityUid target,
        NanoChatCardComponent card)
    {
        _audio.PlayPredicted(ent.Comp.SoundScan, target, args.Args.User);
        _popup.PopupCursor(Loc.GetString("log-probe-scan-nanochat", ("card", target)), args.Args.User);

        ent.Comp.PulledAccessLogs.Clear();

        ent.Comp.ScannedNanoChatData = new NanoChatData(
            new Dictionary<uint, NanoChatRecipient>(card.Recipients),
            new Dictionary<uint, List<NanoChatMessage>>(card.Messages),
            card.Number,
            GetNetEntity(target)
        );

        Dirty(ent);
        UpdateUiState(ent, args.Loader);
    }
}
