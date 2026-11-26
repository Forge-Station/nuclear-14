namespace Content.Client.Paper.UI;

// Extends the existing paper UI without changing its state or duplicating its behavior.
public sealed partial class PaperBoundUserInterface
{
    public override void Update()
    {
        base.Update();
        _window?.SetSurfaceEntity(Owner);
    }
}
