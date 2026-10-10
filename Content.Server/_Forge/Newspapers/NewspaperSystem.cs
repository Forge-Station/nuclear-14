using Content.Server.Administration.Logs;
using Content.Server.Materials;
using Content.Server.Popups;
using Content.Shared._Forge.Newspapers;
using Content.Shared._Forge.Paper;
using Content.Shared.Database;
using Content.Shared.Materials;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using System.Linq;
using Content.Server._Forge.Photo;
using Content.Shared.Interaction;
using System.Security.Cryptography;
using Robust.Shared.Timing;
using Content.Shared.GameTicking;

namespace Content.Server._Forge.Newspapers;

public sealed class NewspaperSystem : EntitySystem
{
    private const int MaxPrintCount = 10;
    private const int MaxPhotoBytes = 4 * 1024 * 1024;
    private readonly Dictionary<EntityUid, (TimeSpan Start, int Count)> _imageRequests = new();
    private readonly Dictionary<EntityUid, (TimeSpan Start, int Count)> _draftRequests = new();
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MaterialStorageSystem _materials = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _imageRequests.Clear(); _draftRequests.Clear(); });

        SubscribeLocalEvent<NewspaperDeskComponent, AfterActivatableUIOpenEvent>(OnDeskOpen);
        SubscribeLocalEvent<NewspaperDeskComponent, MaterialAmountChangedEvent>(OnMaterialChanged);
        SubscribeLocalEvent<NewspaperDeskComponent, InteractUsingEvent>(OnAttachPhoto,
            before: new[] { typeof(MaterialStorageSystem) });
        Subs.BuiEvents<NewspaperDeskComponent>(NewspaperDeskUiKey.Key, subs =>
        {
            subs.Event<NewspaperSaveDraftMessage>(OnSaveDraft);
            subs.Event<NewspaperArchiveRequestMessage>(OnArchiveRequested);
            subs.Event<NewspaperDeleteEditionMessage>(OnDeleteEdition);
            subs.Event<NewspaperSwitchPublicationMessage>(OnSwitchPublication);
            subs.Event<NewspaperSaveTemplateMessage>(OnSaveTemplate);
            subs.Event<NewspaperPublishMessage>(OnPublish);
            subs.Event<NewspaperPrintMessage>(OnPrint);
            subs.Event<NewspaperImageRequestMessage>(OnDeskImageRequested);
            subs.Event<NewspaperSelectImageMessage>(OnSelectImage);
            subs.Event<NewspaperForgetImageMessage>(OnForgetImage);
        });

        SubscribeLocalEvent<NewspaperCopyComponent, AfterActivatableUIOpenEvent>(OnCopyOpen);
        Subs.BuiEvents<NewspaperCopyComponent>(NewspaperCopyUiKey.Key, subs =>
            subs.Event<NewspaperImageRequestMessage>(OnCopyImageRequested));
    }

    // UI state and printed copies.
    private void OnDeskOpen(EntityUid uid, NewspaperDeskComponent component, AfterActivatableUIOpenEvent args)
    {
        UpdateDeskUi(uid, component);
    }

    private void OnMaterialChanged(EntityUid uid, NewspaperDeskComponent component, ref MaterialAmountChangedEvent args)
    {
        UpdateDeskUi(uid, component);
    }

    private void UpdateDeskUi(EntityUid uid, NewspaperDeskComponent component)
    {
        if (component.PublicationDrafts.Count == 0)
        {
            component.Draft.PublicationId = ++component.NextPublicationId;
            component.PublicationDrafts[component.Draft.PublicationId] = component.Draft;
            foreach (var edition in component.Editions) edition.PublicationId = component.Draft.PublicationId;
        }
        component.NextEditionId = Math.Max(component.NextEditionId,
            component.Editions.Select(e => e.ArchiveId).DefaultIfEmpty(0).Max());
        foreach (var edition in component.Editions)
        {
            if (edition.ArchiveId <= 0) edition.ArchiveId = ++component.NextEditionId;
            component.LastEditionNumbers[edition.PublicationId] = Math.Max(edition.Number,
                component.LastEditionNumbers.GetValueOrDefault(edition.PublicationId));
        }
        foreach (var (id, data) in component.Photos)
            if (!component.PhotoKeys.ContainsKey(id)) component.PhotoKeys[id] = Convert.ToHexString(SHA256.HashData(data));
        foreach (var id in component.PhotoKeys.Keys.Where(id => !component.Photos.ContainsKey(id)).ToArray()) component.PhotoKeys.Remove(id);
        var paperCount = component.PaperPerCopy > 0
            ? _materials.GetMaterialAmount(uid, component.PaperMaterial) / component.PaperPerCopy : 0;
        _ui.SetUiState(uid, NewspaperDeskUiKey.Key,
            new NewspaperDeskUiState(component.Draft, component.Editions.Select(NewspaperStorage.Summary).ToArray(), paperCount, component.Templates.ToArray(),
                component.PublicationDrafts.Select(p => new NewspaperPublicationInfo(p.Key, p.Value.Name,
                    NewspaperLayout.NextEditionNumber(component.Editions, p.Key, component.LastEditionNumbers.GetValueOrDefault(p.Key)))).ToArray(),
                component.Draft.PhotoIds.Concat(component.BufferedPhotos).Distinct().Where(component.PhotoKeys.ContainsKey).ToDictionary(id => id, id => component.PhotoKeys[id]),
                component.BufferedPhotos.ToArray(), component.Revision, NewspaperStorage.LatestMatches(component.Editions, component.Draft)));
    }

    private void OnCopyOpen(EntityUid uid, NewspaperCopyComponent component, AfterActivatableUIOpenEvent args)
    {
        if (component.CachedUiState == null)
        {
            if (component.ImageData != null && component.PhotoKey.Length == 0)
                component.PhotoKey = Convert.ToHexString(SHA256.HashData(component.ImageData));
            foreach (var (id, data) in component.Photos)
                if (!component.PhotoKeys.ContainsKey(id))
                    component.PhotoKeys[id] = Convert.ToHexString(SHA256.HashData(data));
            component.CachedUiState = new NewspaperCopyUiState(component.Edition,
                TryComp<PaperSurfaceComponent>(uid, out var paper) ? paper.Appearance.Clone() : null, component.PhotoKey, new(component.PhotoKeys));
        }
        _ui.SetUiState(uid, NewspaperCopyUiKey.Key, component.CachedUiState);
    }

    // Request limits.
    private bool AllowImageRequest(EntityUid actor)
        => AllowRequest(_imageRequests, actor);

    private bool AllowRequest(Dictionary<EntityUid, (TimeSpan Start, int Count)> requests, EntityUid actor)
    {
        var now = _timing.RealTime;
        var entry = requests.GetValueOrDefault(actor);
        if (now - entry.Start > TimeSpan.FromSeconds(2)) entry = (now, 0);
        entry.Count++; requests[actor] = entry;
        return entry.Count <= 5;
    }

    // Draft validation and persistence.
    private static bool TryValidate(NewspaperEdition input, bool requireContent, out NewspaperEdition clean)
    {
        clean = new NewspaperEdition();
        if (!NewspaperLayout.IsValid(input, requireContent))
            return false;
        clean = input.Clone();
        NewspaperLayout.ResolvePhotoCaptions(clean);
        if (!NewspaperLayout.IsValid(clean, requireContent)) return false;
        clean.Name = clean.Name.Trim();
        clean.TemplateName = clean.TemplateName.Trim();
        clean.Number = 0;
        clean.ArchiveId = 0;
        foreach (var block in clean.Blocks)
        {
            block.TextKey = null;
            block.DemoKey = null;
        }
        return true;
    }

    private static bool TryValidateActiveDraft(NewspaperDeskComponent component, NewspaperEdition input, bool requireContent, out NewspaperEdition clean)
    {
        if (!TryValidate(input, requireContent, out clean) || input.PublicationId != component.Draft.PublicationId)
            return false;
        var id = clean.PublicationId; var name = clean.Name;
        return string.IsNullOrEmpty(name) || !component.PublicationDrafts.Values.Any(d =>
            d.PublicationId != id && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryReadDraft(Entity<NewspaperDeskComponent> ent, NewspaperEdition input,
        EntityUid actor, bool requireContent, out NewspaperEdition draft)
    {
        if (!TryValidateActiveDraft(ent.Comp, input, requireContent, out draft))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-invalid-content"), ent, actor);
            return false;
        }
        draft.PhotoId = ent.Comp.Draft.PhotoId;
        var allowed = ent.Comp.BufferedPhotos.Concat(ent.Comp.Draft.PhotoIds).ToHashSet();
        if (draft.PhotoIds.Any(id => !allowed.Contains(id)))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-invalid-content"), ent, actor);
            return false;
        }
        return true;
    }

    private static void StoreDraft(NewspaperDeskComponent component, NewspaperEdition draft)
    {
        component.Revision++;
        component.Draft = draft;
        component.PublicationDrafts[draft.PublicationId] = draft;
    }

    private void Reply(Entity<NewspaperDeskComponent> ent, EntityUid actor, int requestId, string error = "") =>
        _ui.ServerSendUiMessage(ent.Owner, NewspaperDeskUiKey.Key,
            new NewspaperDraftResultMessage(requestId, ent.Comp.Revision, error), actor);

    private bool ReadRequest(Entity<NewspaperDeskComponent> ent, NewspaperEdition input, EntityUid actor,
        bool requireContent, long revision, int requestId, out NewspaperEdition draft)
    {
        draft = new();
        if (!AllowRequest(_draftRequests, actor))
        { Reply(ent, actor, requestId, "newspaper-request-wait"); return false; }
        if (revision != ent.Comp.Revision)
        { Reply(ent, actor, requestId, "newspaper-save-conflict"); return false; }
        if (TryReadDraft(ent, input, actor, requireContent, out draft)) return true;
        Reply(ent, actor, requestId, "newspaper-invalid-content");
        return false;
    }

    private void OnSaveDraft(Entity<NewspaperDeskComponent> ent, ref NewspaperSaveDraftMessage msg)
    {
        if (!ReadRequest(ent, msg.Draft, msg.Actor, false, msg.Revision, msg.RequestId, out var draft)) return;

        StoreDraft(ent.Comp, draft);
        UpdateDeskUi(ent, ent.Comp);
        Reply(ent, msg.Actor, msg.RequestId);
        _popup.PopupEntity(Loc.GetString("newspaper-draft-saved"), ent, msg.Actor);
    }

    private void OnSwitchPublication(Entity<NewspaperDeskComponent> ent, ref NewspaperSwitchPublicationMessage msg)
    {
        if (!ReadRequest(ent, msg.Draft, msg.Actor, false, msg.Revision, msg.RequestId, out var draft)) return;
        NewspaperEdition target;
        if (msg.PublicationId == 0)
        {
            var name = msg.Name?.Trim() ?? "";
            // The initial unnamed draft becomes the first publication, preserving its layout and text.
            if (name.Length is >= 1 and <= 80 && ent.Comp.PublicationDrafts.Count == 1 &&
                string.IsNullOrWhiteSpace(ent.Comp.Draft.Name) && ent.Comp.Editions.Count == 0)
            {
                draft.Name = name;
                StoreDraft(ent.Comp, draft);
                UpdateDeskUi(ent, ent.Comp);
                Reply(ent, msg.Actor, msg.RequestId);
                return;
            }
            if (name.Length is < 1 or > 80 || ent.Comp.PublicationDrafts.Count >= NewspaperLayout.MaxPublications ||
                string.Equals(draft.Name, name, StringComparison.OrdinalIgnoreCase) ||
                ent.Comp.PublicationDrafts.Values.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _popup.PopupEntity(Loc.GetString("newspaper-publication-invalid"), ent, msg.Actor);
                Reply(ent, msg.Actor, msg.RequestId, "newspaper-publication-invalid");
                return;
            }
            target = new NewspaperEdition { Name = name, PublicationId = ++ent.Comp.NextPublicationId };
        }
        else if (!ent.Comp.PublicationDrafts.TryGetValue(msg.PublicationId, out target!))
        { Reply(ent, msg.Actor, msg.RequestId, "newspaper-publication-invalid"); return; }
        StoreDraft(ent.Comp, draft);
        StoreDraft(ent.Comp, target.PublicationId == draft.PublicationId ? draft : target);
        PrunePhotos(ent.Comp);
        UpdateDeskUi(ent, ent.Comp);
        Reply(ent, msg.Actor, msg.RequestId);
    }

    private void OnSaveTemplate(Entity<NewspaperDeskComponent> ent, ref NewspaperSaveTemplateMessage msg)
    {
        if (!AllowRequest(_draftRequests, msg.Actor)) return;
        if (!TryValidate(msg.Template, false, out var template) || string.IsNullOrWhiteSpace(template.TemplateName))
            return;
        var index = ent.Comp.Templates.FindIndex(t => t.TemplateName == template.TemplateName);
        if (index < 0 && ent.Comp.Templates.Count >= NewspaperLayout.MaxTemplates)
        {
            _popup.PopupEntity(Loc.GetString("newspaper-template-limit"), ent, msg.Actor);
            return;
        }
        template.PhotoId = -1;
        foreach (var block in template.Blocks.Where(b => b.Kind == NewspaperBlockKind.Photo)) block.PhotoId = -1;
        if (index < 0)
            ent.Comp.Templates.Add(template);
        else
            ent.Comp.Templates[index] = template;
        UpdateDeskUi(ent, ent.Comp);
        _popup.PopupEntity(Loc.GetString("newspaper-template-saved"), ent, msg.Actor);
    }

    // Photographs.
    private void OnAttachPhoto(EntityUid uid, NewspaperDeskComponent component, InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<PhotoCardComponent>(args.Used, out var photo))
            return;
        args.Handled = true;
        if (photo.ImageData == null || photo.ImageData.Length > MaxPhotoBytes || !AllowImageRequest(args.User))
            return;
        var key = Convert.ToHexString(SHA256.HashData(photo.ImageData));
        var existing = component.PhotoKeys.FirstOrDefault(p => p.Value == key);
        if (existing.Key != 0 && component.BufferedPhotos.Contains(existing.Key))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-photo-already-buffered"), uid, args.User);
            return;
        }
        var bytes = component.BufferedPhotos.Sum(id => (long)(component.Photos.GetValueOrDefault(id)?.Length ?? 0));
        if (!NewspaperStorage.CanImport(component.Photos.Values, existing.Key == 0 ? photo.ImageData.Length : 0))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-photo-storage-full"), uid, args.User);
            return;
        }
        if (!NewspaperPhotoBuffer.CanImport(component.BufferedPhotos.Count, bytes, photo.ImageData.Length))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-photo-buffer-full"), uid, args.User);
            return;
        }
        var photoId = existing.Key != 0 ? existing.Key : ++component.NextPhotoId;
        component.Photos[photoId] = photo.ImageData;
        component.PhotoKeys[photoId] = key;
        component.BufferedPhotos.Add(photoId);
        PrunePhotos(component);
        UpdateDeskUi(uid, component);
        _popup.PopupEntity(Loc.GetString("newspaper-photo-attached"), uid, args.User);
    }

    private static void PrunePhotos(NewspaperDeskComponent component)
    {
        var retained = component.Editions.SelectMany(edition => edition.PhotoIds).ToHashSet();
        retained.UnionWith(component.Draft.PhotoIds);
        retained.UnionWith(component.PublicationDrafts.Values.SelectMany(d => d.PhotoIds));
        retained.UnionWith(component.BufferedPhotos);
        foreach (var id in component.Photos.Keys.Where(id => !retained.Contains(id)).ToArray())
            component.Photos.Remove(id);
    }

    private void OnSelectImage(Entity<NewspaperDeskComponent> ent, ref NewspaperSelectImageMessage msg)
    {
        if (!AllowRequest(_draftRequests, msg.Actor) || msg.PhotoId == ent.Comp.Draft.PhotoId) return;
        if (msg.PhotoId != -1 && !ent.Comp.BufferedPhotos.Contains(msg.PhotoId)) return;
        ent.Comp.Draft.PhotoId = msg.PhotoId;
        ent.Comp.Revision++;
        PrunePhotos(ent.Comp);
        UpdateDeskUi(ent, ent.Comp);
    }

    private void OnForgetImage(Entity<NewspaperDeskComponent> ent, ref NewspaperForgetImageMessage msg)
    {
        if (!AllowRequest(_draftRequests, msg.Actor)) return;
        if (!ent.Comp.BufferedPhotos.Remove(msg.PhotoId)) return;
        ent.Comp.Revision++;
        if (ent.Comp.Draft.PhotoId == msg.PhotoId) ent.Comp.Draft.PhotoId = -1;
        foreach (var block in ent.Comp.Draft.Blocks)
            if (block.PhotoId == msg.PhotoId) block.PhotoId = -1;
        PrunePhotos(ent.Comp);
        UpdateDeskUi(ent, ent.Comp);
    }

    private void OnDeskImageRequested(Entity<NewspaperDeskComponent> ent, ref NewspaperImageRequestMessage msg)
    {
        if (!AllowImageRequest(msg.Actor) || msg.Edition < 0)
            return;
        var archiveId = msg.Edition;
        var edition = archiveId == 0 ? ent.Comp.Draft : ent.Comp.Editions.FirstOrDefault(e => e.ArchiveId == archiveId);
        if (edition == null) return;
        var photoId = msg.PhotoId < 0 ? edition.PhotoId : msg.PhotoId;
        if (!edition.PhotoIds.Contains(photoId) && !(msg.Edition == 0 && ent.Comp.BufferedPhotos.Contains(photoId))) return;
        if (ent.Comp.Photos.TryGetValue(photoId, out var data) && data.Length <= MaxPhotoBytes)
            _ui.ServerSendUiMessage(ent.Owner, NewspaperDeskUiKey.Key,
                new NewspaperImageMessage(photoId, data, ent.Comp.PhotoKeys.GetValueOrDefault(photoId, "")), msg.Actor);
    }

    private void OnCopyImageRequested(Entity<NewspaperCopyComponent> ent, ref NewspaperImageRequestMessage msg)
    {
        if (!AllowImageRequest(msg.Actor)) return;
        var id = msg.PhotoId < 0 ? ent.Comp.Edition.PhotoId : msg.PhotoId;
        if (!ent.Comp.Edition.PhotoIds.Contains(id)) return;
        var data = ent.Comp.Photos.GetValueOrDefault(id) ?? (id == ent.Comp.Edition.PhotoId ? ent.Comp.ImageData : null);
        if (data is { Length: <= MaxPhotoBytes })
            _ui.ServerSendUiMessage(ent.Owner, NewspaperCopyUiKey.Key,
                new NewspaperImageMessage(id, data, ent.Comp.PhotoKeys.GetValueOrDefault(id, ent.Comp.PhotoKey)), msg.Actor);
    }

    // Archive, publishing and printing.
    private void OnArchiveRequested(Entity<NewspaperDeskComponent> ent, ref NewspaperArchiveRequestMessage msg)
    {
        if (!AllowRequest(_draftRequests, msg.Actor) || msg.Edition < 1) return;
        var archiveId = msg.Edition;
        var edition = ent.Comp.Editions.FirstOrDefault(e => e.ArchiveId == archiveId);
        if (edition == null) return;
        _ui.ServerSendUiMessage(ent.Owner, NewspaperDeskUiKey.Key, new NewspaperArchiveMessage(msg.Edition, edition,
            edition.PhotoIds.Where(ent.Comp.PhotoKeys.ContainsKey).ToDictionary(id => id, id => ent.Comp.PhotoKeys[id])), msg.Actor);
    }

    private void OnDeleteEdition(Entity<NewspaperDeskComponent> ent, ref NewspaperDeleteEditionMessage msg)
    {
        if (!AllowRequest(_draftRequests, msg.Actor)) return;
        if (!NewspaperStorage.TryRemoveEdition(ent.Comp.Editions, msg.Edition, out var edition) || edition == null)
        { UpdateDeskUi(ent, ent.Comp); return; }
        PrunePhotos(ent.Comp);
        UpdateDeskUi(ent, ent.Comp);
        _adminLogger.Add(LogType.Chat, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor):actor} removed newspaper edition {edition.Number} ({edition.Name}) from {ToPrettyString(ent):desk}");
        _popup.PopupEntity(Loc.GetString("newspaper-archive-deleted", ("edition", edition.Number)), ent, msg.Actor);
    }

    private void OnPublish(Entity<NewspaperDeskComponent> ent, ref NewspaperPublishMessage msg)
    {
        if (!ReadRequest(ent, msg.Draft, msg.Actor, true, msg.Revision, msg.RequestId, out var draft)) return;
        if (!NewspaperStorage.CanPublish(ent.Comp.Editions, draft, _timing.RealTime, ent.Comp.NextPublish))
        {
            Reply(ent, msg.Actor, msg.RequestId, ent.Comp.Editions.Count >= NewspaperStorage.MaxEditions
                ? "newspaper-archive-full" : _timing.RealTime < ent.Comp.NextPublish
                    ? "newspaper-publish-wait" : "newspaper-publish-unchanged-tip");
            return;
        }
        ent.Comp.NextPublish = _timing.RealTime + ent.Comp.PublishCooldown;

        StoreDraft(ent.Comp, draft);
        var published = draft.Clone();
        published.Number = NewspaperLayout.NextEditionNumber(ent.Comp.Editions, draft.PublicationId,
            ent.Comp.LastEditionNumbers.GetValueOrDefault(draft.PublicationId));
        ent.Comp.LastEditionNumbers[draft.PublicationId] = published.Number;
        published.ArchiveId = ++ent.Comp.NextEditionId;
        ent.Comp.Editions.Add(published);
        _adminLogger.Add(LogType.Chat, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor):actor} approved newspaper edition {published.Number} at {ToPrettyString(ent):desk}: {published.Name} / {string.Join(" / ", published.Blocks.Where(b => b.Kind == NewspaperBlockKind.Text).Select(b => b.Text))}");
        UpdateDeskUi(ent, ent.Comp);
        Reply(ent, msg.Actor, msg.RequestId);
        _popup.PopupEntity(Loc.GetString("newspaper-published", ("edition", published.Number)), ent, msg.Actor);
    }

    private void OnPrint(Entity<NewspaperDeskComponent> ent, ref NewspaperPrintMessage msg)
    {
        if (!AllowRequest(_draftRequests, msg.Actor)) return;
        if (!Enum.IsDefined(msg.Paper) || msg.Count is < 1 or > MaxPrintCount || msg.Edition < 1)
            return;
        var archiveId = msg.Edition;
        var edition = ent.Comp.Editions.FirstOrDefault(e => e.ArchiveId == archiveId);
        if (edition == null) return;
        if (_timing.RealTime < ent.Comp.NextPrint)
        {
            _popup.PopupEntity(Loc.GetString("newspaper-print-wait"), ent, msg.Actor);
            return;
        }

        if (ent.Comp.PaperPerCopy <= 0 || ent.Comp.PaperPerCopy > int.MaxValue / MaxPrintCount)
            return;
        var cost = msg.Count * ent.Comp.PaperPerCopy;
        if (!_materials.TryChangeMaterialAmount(ent, ent.Comp.PaperMaterial, -cost))
        {
            _popup.PopupEntity(Loc.GetString("newspaper-no-paper"), ent, msg.Actor);
            return;
        }

        ent.Comp.NextPrint = _timing.RealTime + ent.Comp.PrintCooldown;
        for (var i = 0; i < msg.Count; i++)
        {
            var copy = Spawn(ent.Comp.PrintedPrototype, Transform(ent).Coordinates);
            var component = Comp<NewspaperCopyComponent>(copy);
            component.Edition = edition;
            if (TryComp<PaperSurfaceComponent>(copy, out var surface))
            {
                surface.Appearance.Profile = NewspaperLayout.PaperProfile(msg.Paper);
                Dirty(copy, surface);
            }
            component.Photos = edition.PhotoIds.Where(ent.Comp.Photos.ContainsKey).ToDictionary(id => id, id => ent.Comp.Photos[id]);
            component.PhotoKeys = edition.PhotoIds.Where(ent.Comp.PhotoKeys.ContainsKey).ToDictionary(id => id, id => ent.Comp.PhotoKeys[id]);
            _meta.SetEntityName(copy, Loc.GetString("newspaper-printed-name",
                ("name", edition.Name), ("edition", edition.Number)));
        }

        UpdateDeskUi(ent, ent.Comp);
        _popup.PopupEntity(Loc.GetString("newspaper-printed", ("count", msg.Count)), ent, msg.Actor);
    }
}
