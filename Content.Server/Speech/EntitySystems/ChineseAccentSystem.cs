using System.Text.RegularExpressions;
using Content.Server.Speech;
using Content.Server.Speech.Components;

namespace Content.Server.Speech.EntitySystems;

public sealed class ChineseAccentSystem : EntitySystem
{
    [Dependency] private readonly ReplacementAccentSystem _replacement = default!;

    private static readonly Regex RegexR = new(@"р", RegexOptions.IgnoreCase);
    private static readonly Regex RegexSh = new(@"ш", RegexOptions.IgnoreCase);
    private static readonly Regex RegexZh = new(@"ж", RegexOptions.IgnoreCase);
    private static readonly Regex RegexCh = new(@"ч", RegexOptions.IgnoreCase);
    private static readonly Regex RegexY = new(@"ы", RegexOptions.IgnoreCase);
    private static readonly Regex RegexSoftSign = new(@"ь", RegexOptions.IgnoreCase);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ChineseAccentComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, ChineseAccentComponent component, AccentGetEvent args)
    {
        args.Message = Accentuate(args.Message);
    }

    public string Accentuate(string message)
    {
        var words = message.Split(' ');
        var accentuatedWords = new List<string>(words.Length);

        foreach (var word in words)
        {
            var accentuatedWord = _replacement.ApplyReplacements(word, "chinese");

            if (accentuatedWord == word)
                accentuatedWord = ApplyPhoneticReplacements(accentuatedWord);

            if (Random.Shared.NextDouble() < 0.015)
                accentuatedWord += " э";

            accentuatedWords.Add(accentuatedWord);
        }

        return string.Join(" ", accentuatedWords);
    }

    private static string ApplyPhoneticReplacements(string word)
    {
        word = RegexR.Replace(word, match => ReplaceWithCase(match.Value, "л"));
        word = RegexSh.Replace(word, match => ReplaceWithCase(match.Value, "с"));
        word = RegexZh.Replace(word, match => ReplaceWithCase(match.Value, "дж"));
        word = RegexCh.Replace(word, match => ReplaceWithCase(match.Value, "ц"));
        word = RegexY.Replace(word, match => ReplaceWithCase(match.Value, "и"));
        word = RegexSoftSign.Replace(word, match => ReplaceWithCase(match.Value, "и"));
        return word;
    }

    private static string ReplaceWithCase(string original, string replacement)
    {
        return original.ToUpperInvariant() == original
            ? replacement.ToUpperInvariant()
            : original.ToLowerInvariant() == original
                ? replacement.ToLowerInvariant()
                : replacement;
    }
}
