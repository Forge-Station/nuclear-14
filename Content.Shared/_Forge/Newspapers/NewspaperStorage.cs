using System.Linq;

namespace Content.Shared._Forge.Newspapers;

/// <summary>Per-desk bounds include archived photographs, not just the import buffer.</summary>
public static class NewspaperStorage
{
    public const int MaxEditions = 64;
    public const long MaxPhotoBytes = 64L * 1024 * 1024;

    public static bool TryRemoveEdition(List<NewspaperEdition> editions, int archiveId, out NewspaperEdition? removed)
    {
        removed = archiveId > 0 ? editions.FirstOrDefault(e => e.ArchiveId == archiveId) : null;
        return removed != null && editions.Remove(removed);
    }

    public static bool CanImport(IEnumerable<byte[]> photos, int additionalBytes) =>
        additionalBytes >= 0 && photos.Sum(data => (long)data.Length) + additionalBytes <= MaxPhotoBytes;

    public static bool CanPublish(IReadOnlyList<NewspaperEdition> editions, NewspaperEdition draft, TimeSpan now, TimeSpan nextPublish) =>
        editions.Count < MaxEditions && now >= nextPublish && !LatestMatches(editions, draft);

    public static bool LatestMatches(IEnumerable<NewspaperEdition> editions, NewspaperEdition draft) =>
        editions.LastOrDefault(e => e.PublicationId == draft.PublicationId) is { } last && SameContent(last, draft);

    public static NewspaperEdition Summary(NewspaperEdition source) => new()
    {
        Name = source.Name, Number = source.Number, PublicationId = source.PublicationId, ArchiveId = source.ArchiveId,
        Width = source.Width, Height = source.Height, Pages = source.Pages,
        Blocks = source.Blocks.Where(b => b.Kind == NewspaperBlockKind.Text && !b.IsLabel && b.Text.Length > 0)
            .Take(1).Select(b => new NewspaperBlock { Text = b.Text[..Math.Min(b.Text.Length, 80)] }).ToList(),
    };

    public static void RemoveUnavailablePhotos(NewspaperEdition draft, IEnumerable<int> available)
    {
        var ids = available.ToHashSet();
        if (!ids.Contains(draft.PhotoId)) draft.PhotoId = -1;
        foreach (var block in draft.Blocks)
            if (block.PhotoId >= 0 && !ids.Contains(block.PhotoId)) block.PhotoId = -1;
    }

    public static bool IsPhotoRemoval(NewspaperEdition previous, NewspaperEdition current, IEnumerable<int> available)
    {
        var sanitized = previous.Clone();
        RemoveUnavailablePhotos(sanitized, available);
        return SameContent(sanitized, current);
    }

    public static bool SameContent(NewspaperEdition a, NewspaperEdition b, bool comparePhotos = true) =>
        a.PublicationId == b.PublicationId && a.Name.Trim() == b.Name.Trim() && a.TemplateName.Trim() == b.TemplateName.Trim() && a.Width == b.Width && a.Height == b.Height && a.Pages == b.Pages &&
        a.Blocks.Count == b.Blocks.Count && a.Blocks.Zip(b.Blocks).All(pair =>
            pair.First.Kind == pair.Second.Kind && pair.First.Page == pair.Second.Page && pair.First.X == pair.Second.X && pair.First.Y == pair.Second.Y &&
            pair.First.Width == pair.Second.Width && pair.First.Height == pair.Second.Height && pair.First.Text == pair.Second.Text && pair.First.IsLabel == pair.Second.IsLabel &&
            pair.First.FontSize == pair.Second.FontSize && pair.First.Font == pair.Second.Font && pair.First.Ink == pair.Second.Ink && pair.First.Centered == pair.Second.Centered && pair.First.Borders == pair.Second.Borders && pair.First.Grayscale == pair.Second.Grayscale && (!comparePhotos || pair.First.PhotoId == pair.Second.PhotoId) && pair.First.Id == pair.Second.Id && pair.First.CaptionFor == pair.Second.CaptionFor) &&
        (!comparePhotos || a.PhotoId == b.PhotoId);
}
