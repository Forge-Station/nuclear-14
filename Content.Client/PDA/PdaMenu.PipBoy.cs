using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client.PDA;

public sealed partial class PdaMenu
{
    private const string PipBoyStyle = "pipboy-theme";

    private bool _pipBoyLayoutInitialized;
    private bool _pipBoyGreen;
    private Button? _greenButton;
    private Button? _amberButton;
    public event Action<bool>? OnPipBoyColorChanged;

    /// <summary>
    /// Opt-in, window-local skin. Remove pipBoyTheme from the prototype to use the original UI.
    /// The local stylesheet also applies to program fragments added after opening the window.
    /// </summary>
    public void ApplyPipBoyTheme(bool green = false)
    {
        _pipBoyGreen = green;
        var accent = green ? "#80FF80" : "#FFBE62";
        var muted = green ? "#498C49" : "#A57C36";
        var edge = green ? "#426442" : "#735D32";
        var highlight = green ? "#213B21" : "#3B321A";
        var selection = green ? "#315531" : "#554124";
        var bright = green ? "#B3FFAD" : "#FFDA8A";
        ApplyPipBoyFrame(accent, muted);

        var foreground = Color.FromHex(accent);
        var dim = Color.FromHex(muted);
        var normal = PipBoyBox("#191D12", edge);
        var hover = PipBoyBox(highlight, accent);
        var pressed = PipBoyBox(selection, bright);
        var rules = IoCManager.Resolve<IUserInterfaceManager>().Stylesheet?.Rules ?? Array.Empty<StyleRule>();
        Stylesheet = new Stylesheet(rules.Concat(new StyleRule[]
        {
            Element<Label>().Class(PipBoyStyle).Prop("font-color", foreground),
            Element<LineEdit>().Class(PipBoyStyle).Prop("stylebox", normal).Prop("font-color", foreground)
                .Prop("cursor-color", foreground).Prop("selection-color", Color.FromHex(selection)),
            Element<Button>().Class(PipBoyStyle).Prop("stylebox", normal),
            Element<Button>().Class(PipBoyStyle).Pseudo("hover").Prop("stylebox", hover),
            Element<Button>().Class(PipBoyStyle).Pseudo("pressed").Prop("stylebox", pressed),
            Element<Button>().Class(PipBoyStyle).Pseudo("disabled").Prop("stylebox", PipBoyBox("#171A13", "#49452D")),
            Element<PdaProgramItem>().Class(PipBoyStyle).Pseudo("normal").Prop("backgroundColor", Color.FromHex("#191D12")),
            Element<PdaProgramItem>().Class(PipBoyStyle).Pseudo("hover").Prop("backgroundColor", Color.FromHex(highlight)),
            Element<PdaProgramItem>().Class(PipBoyStyle).Pseudo("pressed").Prop("backgroundColor", Color.FromHex(selection)),
            Element<PdaSettingsButton>().Class(PipBoyStyle).Prop("foregroundColor", foreground),
            Element<PdaSettingsButton>().Class(PipBoyStyle).Pseudo("normal").Prop("backgroundColor", Color.FromHex("#191D12")),
            Element<PdaSettingsButton>().Class(PipBoyStyle).Pseudo("hover").Prop("backgroundColor", Color.FromHex(highlight)),
            Element<PdaSettingsButton>().Class(PipBoyStyle).Pseudo("pressed").Prop("backgroundColor", Color.FromHex(selection)),
            Element<Slider>().Class(PipBoyStyle).Prop("background", normal)
                .Prop("fill", new StyleBoxFlat { BackgroundColor = dim })
                .Prop("grabber", PipBoyBox(accent, bright)),
        }).ToArray());

        foreach (var child in NavigationBar.Children)
        {
            if (child is not PdaNavigationButton button)
                continue;

            button.InactiveBgColor = "#11140E";
            button.ActiveBgColor = highlight;
            button.InactiveFgColor = muted;
            button.ActiveFgColor = accent;
            button.BorderThickness = new Thickness(0, 0, 0, 1);
            button.CurrentTabBorderThickness = new Thickness(0, 0, 0, 3);
            button.IsActive = button.IsActive;
            button.IsCurrent = button.IsCurrent;
        }

        if (!_pipBoyLayoutInitialized)
            InitializePipBoyLayout();

        _greenButton!.Pressed = green;
        _amberButton!.Pressed = !green;
        ApplyPipBoyControlStyle(this);
    }

    private void InitializePipBoyLayout()
    {
        _pipBoyLayoutInitialized = true;
        MinSize = new Vector2(640, 480);
        SetSize = new Vector2(640, 480);
        HomeButton.IconTexture = null;
        HomeButton.SetWidth = 100;
        HomeButton.LabelText = Loc.GetString("pipboy-theme-status");
        ProgramListButton.LabelText = Loc.GetString("pipboy-theme-data");
        SettingsButton.LabelText = Loc.GetString("pipboy-theme-settings");
        NavigationBar.MinHeight = 42;
        ProgramList.SeparationOverride = 6;

        // Replace only the decorative footer; retain the address label updated by the PDA system.
        AddressLabel.Orphan();
        ContentFooter.RemoveAllChildren();
        ContentFooter.Margin = new Thickness(10, 0, 10, 0);
        ContentFooter.AddChild(new Label { Text = "ROBCO INDUSTRIES / PIP-BOY", HorizontalExpand = true });
        ContentFooter.AddChild(AddressLabel);
        AccessRingtoneButton.Visible = false;
        var colors = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(8),
            Visible = false,
        };
        var colorButton = new PdaSettingsButton
        {
            Text = Loc.GetString("pipboy-theme-color"),
            Description = Loc.GetString("pipboy-theme-color-description"),
        };
        colorButton.OnPressed += _ => colors.Visible = !colors.Visible;
        _greenButton = new Button { Text = Loc.GetString("pipboy-theme-green"), ToggleMode = true, HorizontalExpand = true };
        _amberButton = new Button { Text = Loc.GetString("pipboy-theme-amber"), ToggleMode = true, HorizontalExpand = true };
        _greenButton.OnPressed += _ => SelectPipBoyColor(true);
        _amberButton.OnPressed += _ => SelectPipBoyColor(false);
        colors.AddChild(_greenButton);
        colors.AddChild(_amberButton);
        Settings.AddChild(colorButton);
        Settings.AddChild(colors);
    }

    private static StyleBoxFlat PipBoyBox(string background, string border)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = Color.FromHex(border),
            BorderThickness = new Thickness(1),
        };
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 4);
        return box;
    }

    private void SelectPipBoyColor(bool green)
    {
        ApplyPipBoyTheme(green);
        OnPipBoyColorChanged?.Invoke(green);
    }

    private void ApplyPipBoyControlStyle(Control control)
    {
        var styled = control.HasStyleClass(PipBoyStyle);
        if (!styled)
            control.AddStyleClass(PipBoyStyle);
        if (control is PdaProgramItem item)
            item.Icon.Modulate = Color.FromHex(_pipBoyGreen ? "#80FF80" : "#FFBE62");

        foreach (var child in control.Children)
            ApplyPipBoyControlStyle(child);

        if (!styled)
            control.OnChildAdded += ApplyPipBoyControlStyle;
    }
}
