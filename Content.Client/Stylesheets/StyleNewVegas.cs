using System.Collections.Generic;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client.Stylesheets;

/// <summary>Amber accents and warm charcoal styling for the lobby.</summary>
public static class StyleNewVegas
{
    public static readonly Color Amber = Color.FromHex("#D8B15D");
    public static readonly Color HeadingColor = Color.FromHex("#A88B5E");

    private static readonly (string State, string Background, string Border)[] ButtonStates =
        new[]
        {
            (ContainerButton.StylePseudoClassNormal, "#29231C", "#80663C"),
            (ContainerButton.StylePseudoClassHover, "#3B3023", "#D8B15D"),
            (ContainerButton.StylePseudoClassPressed, "#51402A", "#E8C980"),
            (ContainerButton.StylePseudoClassDisabled, "#211F1B", "#403A30"),
        };

    public static IEnumerable<StyleRule> Rules()
    {
        yield return Element<PanelContainer>().Class("NewVegasPanel")
            .Prop(PanelContainer.StylePropertyPanel, Box("#201C17CC", "#80663C", 10));
        yield return Element<Label>().Class("NewVegasText")
            .Prop(Label.StylePropertyFontColor, Amber);
        yield return Element<ContainerButton>().Class("NewVegasButton")
            .Prop(ContainerButton.StylePropertyStyleBox, ButtonBox("#29231C", "#80663C"));

        foreach (var (state, background, border) in ButtonStates)
        {
            yield return Element<ContainerButton>().Class("NewVegasButton").Pseudo(state)
                .Prop(Control.StylePropertyModulateSelf, Color.White)
                .Prop(ContainerButton.StylePropertyStyleBox, ButtonBox(background, border));
        }
    }

    public static Stylesheet CharacterSetupStylesheet(Stylesheet baseSheet)
    {
        // Keep font, icon and layout rules; override the palette only inside character setup.
        var rules = new List<StyleRule>(baseSheet.Rules);
        void Add(StyleRule rule) => rules.Add(new StyleRule(new LocalThemeSelector(rule.Selector), rule.Properties));

        Add(Element<PanelContainer>().Prop(PanelContainer.StylePropertyPanel, Box("#201C17", "#80663C", 0)));
        Add(Element<Label>().Prop(Label.StylePropertyFontColor, Amber));
        // Reserve a measured gap between dropdown text and its arrow at every UI scale.
        Add(Child().Parent(Element<OptionButton>()).Child(Element<BoxContainer>())
            .Prop(BoxContainer.StylePropertySeparation, 8));
        Add(Element<LineEdit>()
            .Prop(LineEdit.StylePropertyStyleBox, Box("#191610", "#80663C", 5))
            .Prop(LineEdit.StylePropertyCursorColor, Amber)
            .Prop(LineEdit.StylePropertySelectionColor, Color.FromHex("#80663C")));
        Add(Element<TextEdit>()
            .Prop(TextEdit.StylePropertyCursorColor, Amber)
            .Prop(TextEdit.StylePropertySelectionColor, Color.FromHex("#80663C")));

        foreach (var (state, background, border) in ButtonStates)
        {
            Add(Element<ContainerButton>().Pseudo(state)
                .Prop(Control.StylePropertyModulateSelf, Color.White)
                .Prop(ContainerButton.StylePropertyStyleBox, ButtonBox(background, border)));
        }

        return new Stylesheet(rules);
    }

    // Higher priority than the inherited global theme, without changing controls or their state classes.
    private sealed class LocalThemeSelector(Selector selector) : Selector
    {
        public override bool Matches(Control control) => selector.Matches(control);
        public override StyleSpecificity CalculateSpecificity() =>
            selector.CalculateSpecificity() + new StyleSpecificity(1, 0, 0);
    }

    public static StyleBox HeadingBox()
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = Color.Transparent,
            BorderColor = HeadingColor,
            BorderThickness = new Thickness(0, 0, 2, 2),
        };
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 10);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 2);
        return box;
    }

    private static StyleBox ButtonBox(string background, string border)
    {
        var box = new RoundedButtonBox(Color.FromHex(background), Color.FromHex(border));
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, 6);
        return box;
    }

    // Use the same rectangle rendering path for both the body and rounded edges.
    private sealed class RoundedButtonBox(Color background, Color border) : StyleBox
    {
        protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
        {
            var radius = MathF.Min(10 * uiScale, MathF.Min(box.Width, box.Height) / 2);
            Fill(handle, box, radius, border);
            var inner = new UIBox2(box.Left + uiScale, box.Top + uiScale,
                box.Right - uiScale, box.Bottom - uiScale);
            if (inner.Width > 0 && inner.Height > 0)
                Fill(handle, inner, MathF.Max(0, radius - uiScale), background);
        }

        private static void Fill(DrawingHandleScreen handle, UIBox2 box, float radius, Color color)
        {
            if (radius <= 0)
            {
                handle.DrawRect(box, color);
                return;
            }

            handle.DrawRect(new UIBox2(box.Left, box.Top + radius, box.Right, box.Bottom - radius), color);
            // Each one-pixel strip follows the circular edge. Unlike separate corner circles,
            // these use DrawRect throughout, so the fill and corners have identical color handling.
            for (var y = 0f; y < radius; y += 1f)
            {
                var end = MathF.Min(y + 1f, radius);
                var distance = radius - (y + end) / 2f;
                var inset = radius - MathF.Sqrt(MathF.Max(0, radius * radius - distance * distance));
                handle.DrawRect(new UIBox2(box.Left + inset, box.Top + y,
                    box.Right - inset, box.Top + end), color);
                handle.DrawRect(new UIBox2(box.Left + inset, box.Bottom - end,
                    box.Right - inset, box.Bottom - y), color);
            }
        }
    }

    private static StyleBoxFlat Box(string background, string border, int padding)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = Color.FromHex(border),
            BorderThickness = new Thickness(1),
        };
        box.SetContentMarginOverride(StyleBox.Margin.All, padding);
        return box;
    }
}
