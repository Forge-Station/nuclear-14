using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;
using System.Linq;
namespace Content.Shared._Forge.Newspapers;
[Serializable, NetSerializable]
public enum NewspaperInk : byte { Black, Red, Blue, Green }
[Serializable, NetSerializable]
public enum NewspaperBlockKind : byte { Text, Photo, Line, EditionNumber, PublicationName, PageNumber }
[Serializable, NetSerializable]
public enum NewspaperFont : byte { Serif, Sans, SansBold, SerifSemibold, SerifBold, Cambria }
[Serializable, NetSerializable]
public enum NewspaperPaper : byte { White, Yellowed, Recycled }
[Flags, Serializable, NetSerializable]
public enum NewspaperBorders : byte
{
    None = 0, Top = 1, Bottom = 2, Left = 4, Right = 8,
    Horizontal = Top | Bottom, Vertical = Left | Right, All = Horizontal | Vertical,
}
/// <summary>A player-editable rectangle in logical page pixels.</summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class NewspaperBlock
{
    [DataField] public NewspaperBlockKind Kind;
    [DataField] public int Page;
    [DataField] public int X;
    [DataField] public int Y;
    [DataField] public int Width = 200;
    [DataField] public int Height = 80;
    [DataField] public int FontSize = 16;
    [DataField] public NewspaperFont Font;
    [DataField] public NewspaperInk Ink;
    [DataField] public bool Centered;
    [DataField] public NewspaperBorders Borders;
    [DataField] public string Text = "";
    [DataField] public bool IsLabel;
    [DataField] public string Id = "";
    [DataField] public string? CaptionFor;
    [DataField] public bool Grayscale;
    // -2 inherits the legacy edition photograph; -1 explicitly leaves this block empty.
    [DataField] public int PhotoId = -2;
    // Starter prototype localization, resolved when creating a draft.
    [DataField] public string? TextKey;
    [DataField] public string? DemoKey;
    public NewspaperBlock Clone() => (NewspaperBlock)MemberwiseClone();
}
[Serializable, NetSerializable, DataDefinition]
public sealed partial class NewspaperEdition
{
    [DataField] public string Name = "";
    [DataField] public string TemplateName = "";
    [DataField] public int Width = 620;
    [DataField] public int Height = 540;
    [DataField] public int Pages = 1;
    [DataField] public List<NewspaperBlock> Blocks = new();
    [DataField] public int PhotoId = -1;
    [DataField] public int Number;
    [DataField] public int PublicationId;
    [DataField] public int ArchiveId;
    public int PhotoFor(NewspaperBlock block) => block.PhotoId == -2 ? PhotoId : block.PhotoId;
    public IEnumerable<int> PhotoIds => Blocks.Where(b => b.Kind == NewspaperBlockKind.Photo)
        .Select(PhotoFor).Append(PhotoId).Where(id => id >= 0).Distinct();
    public IEnumerable<int> PhotoIdsForPage(int page) => Blocks
        .Where(b => b.Kind == NewspaperBlockKind.Photo && b.Page == page)
        .Select(PhotoFor).Where(id => id >= 0).Distinct();
    public NewspaperEdition Clone() => new()
    {
        Name = Name, TemplateName = TemplateName, Width = Width, Height = Height,
        Pages = Pages, PhotoId = PhotoId, Number = Number, PublicationId = PublicationId, ArchiveId = ArchiveId,
        Blocks = Blocks.Select(block => block.Clone()).ToList(),
    };
}
[Prototype("newspaperTemplate")]
public sealed partial class NewspaperTemplatePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public string Name = "";
    [DataField(required: true)] public NewspaperEdition Layout = new();
    [DataField] public bool Default;
    [DataField] public int Order;
}
/// <summary>Limits shared by server validation and the layout editor.</summary>
public static class NewspaperLayout
{
    public static string PaperProfile(NewspaperPaper paper) => paper switch
    {
        NewspaperPaper.Yellowed => "N14YellowedNewsprint",
        NewspaperPaper.Recycled => "N14RecycledPaper",
        _ => "N14WhiteNewsprint",
    };
    public const int MaxBlocks = 96;
    public const int MaxTemplates = 16;
    public const int MaxPublications = 16;
    public static int NextEditionNumber(IEnumerable<NewspaperEdition> editions, int publicationId, int lastNumber = 0) =>
        Math.Max(lastNumber, editions.Where(e => e.PublicationId == publicationId).Select(e => e.Number).DefaultIfEmpty(0).Max()) + 1;
    public const int MaxTextLength = 4000;
    public const int MaxTotalText = 16000;
    public const int MinPageSize = 300;
    public const int MaxPageSize = 1200;
    public static NewspaperEdition ClearContents(NewspaperEdition source)
    {
        var clean = source.Clone();
        clean.PhotoId = -1;
        foreach (var block in clean.Blocks)
        {
            if (block.Kind == NewspaperBlockKind.Text && !block.IsLabel && block.TextKey == null) block.Text = "";
            if (block.Kind == NewspaperBlockKind.Photo) block.PhotoId = -1;
        }
        return clean;
    }
    public static void ResolvePhotoCaptions(NewspaperEdition edition)
    {
        foreach (var caption in edition.Blocks.Where(b => b.CaptionFor != null))
        {
            var photo = edition.Blocks.FirstOrDefault(b => b.Id == caption.CaptionFor && b.Kind == NewspaperBlockKind.Photo && b.Page == caption.Page);
            if (photo == null) continue;
            caption.X = photo.X; caption.Y = photo.Y + photo.Height + 6; caption.Width = photo.Width;
        }
    }

    public static bool IsValid(NewspaperEdition? edition, bool requireContent = false)
    {
        if (edition == null || edition.Name == null || edition.TemplateName == null || edition.Blocks == null ||
            edition.Name.Length > 80 || edition.TemplateName.Length > 80 ||
            edition.Width is < MinPageSize or > MaxPageSize || edition.Height is < MinPageSize or > MaxPageSize ||
            edition.Pages is < 1 or > 2 || edition.Blocks.Count > MaxBlocks)
            return false;
        var total = 0;
        var hasText = false;
        foreach (var block in edition.Blocks)
        {
            if (block == null || block.Id == null || block.Id.Length > 64 || block.CaptionFor?.Length > 64 || block.Text == null || !Enum.IsDefined(block.Kind) ||
                !Enum.IsDefined(block.Font) || !Enum.IsDefined(block.Ink) ||
                (block.Borders & ~NewspaperBorders.All) != 0 ||
                block.Page < 0 || block.Page >= edition.Pages || block.X < 0 || block.Y < 0 ||
                block.Width < 1 || block.Height < 1 || block.Width > edition.Width || block.Height > edition.Height ||
                block.X > edition.Width - block.Width || block.Y > edition.Height - block.Height ||
                block.PhotoId < -2 || block.Kind != NewspaperBlockKind.Photo && block.PhotoId != -2 ||
                block.FontSize is < 1 or > 72 || block.Text.Length > MaxTextLength)
                return false;
            total += block.Text.Length;
            hasText |= block.Kind == NewspaperBlockKind.Text && !block.IsLabel && !string.IsNullOrWhiteSpace(block.Text);
        }
        var ids = edition.Blocks.Where(b => b.Id.Length > 0).Select(b => b.Id).ToArray();
        if (ids.Distinct().Count() != ids.Length || edition.Blocks.Any(b => b.CaptionFor != null &&
            (b.Kind != NewspaperBlockKind.Text || !edition.Blocks.Any(p => p.Kind == NewspaperBlockKind.Photo && p.Id == b.CaptionFor && p.Id.Length > 0 && p.Page == b.Page))))
            return false;
        return total <= MaxTotalText && (!requireContent || (!string.IsNullOrWhiteSpace(edition.Name) && hasText));
    }
}
