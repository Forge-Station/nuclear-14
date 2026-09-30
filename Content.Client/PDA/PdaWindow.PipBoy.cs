using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.PDA;

public partial class PdaWindow
{
    protected void ApplyPipBoyFrame(string accent, string muted)
    {
        Background.ModulateSelfOverride = Color.White;
        Background.PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#302F25"),
            BorderColor = Color.FromHex("#57533C"),
            BorderThickness = new Thickness(3),
        };
        Border.PanelOverride = new StyleBoxFlat { BackgroundColor = Color.Transparent };
        AccentH.Visible = false;
        AccentV.Visible = false;
        PipBoyHeading.Visible = true;
        PipBoyHeading.FontColorOverride = Color.FromHex(accent);
        ManufacturerLabel.Text = "PERSONAL INFORMATION PROCESSOR";
        ManufacturerLabel.FontColorOverride = Color.FromHex(muted);
        ContentBorder.PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex(muted) };
        ContentBackground.PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#11140E") };
    }
}
