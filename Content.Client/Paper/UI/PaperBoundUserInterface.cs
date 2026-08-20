using Content.Client.Language.Systems; // Forge-Change
using Content.Client.Paper; // Forge-Change
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using static Content.Shared.Paper.SharedPaperComponent;

namespace Content.Client.Paper.UI;

[UsedImplicitly]
public sealed class PaperBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private PaperWindow? _window;

    private PaperAction _mode; // Forge-Change

    public PaperBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<PaperWindow>();
        _window.OnSaved += InputOnTextEntered;
        EntMan.System<LanguageSystem>().OnLanguagesChanged += RefreshLanguageOptions; // Forge-Change

        if (EntMan.TryGetComponent<PaperVisualsComponent>(Owner, out var visuals))
        {
            _window.InitVisuals(Owner, visuals);
        }
    }

    // Forge-Change-Start
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            EntMan.System<LanguageSystem>().OnLanguagesChanged -= RefreshLanguageOptions;

        base.Dispose(disposing);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        var paperState = (PaperBoundUserInterfaceState) state;
        _mode = paperState.Mode;
        var visuals = EntMan.System<PaperLanguageVisualsSystem>();

        if (visuals.TryFormatForReader(Owner, paperState, out var formatted))
            paperState = formatted;

        _window?.Populate(paperState);
        RefreshLanguageOptions();
    }

    private void RefreshLanguageOptions()
    {
        if (_window == null)
            return;

        if (_mode != PaperAction.Write)
        {
            _window.SetLanguageOptions([], null);
            return;
        }

        var langs = EntMan.System<PaperLanguageVisualsSystem>().GetWritableLanguages(Owner, out var selected);
        _window.SetLanguageOptions(langs, selected);
    }
    // Forge-Change-End

    private void InputOnTextEntered(string text)
    {
        SendMessage(new PaperInputTextMessage(text, _window?.GetSelectedLanguage())); // Forge-Change

        if (_window != null)
        {
            _window.Input.TextRope = Rope.Leaf.Empty;
            _window.Input.CursorPosition = new TextEdit.CursorPos(0, TextEdit.LineBreakBias.Top);
        }
    }
}
