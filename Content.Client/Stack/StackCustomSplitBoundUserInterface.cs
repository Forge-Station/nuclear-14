// Cherry-picked from space-station-14#32938 courtesy of Ilya246

using Content.Shared.Stacks;
using JetBrains.Annotations;
using Robust.Client.UserInterface;


namespace Content.Client.Stack;


[UsedImplicitly]
public sealed class StackCustomSplitBoundUserInterface : BoundUserInterface
{
    private readonly IEntityManager _entManager;
    private readonly EntityUid _owner;

    [ViewVariables]
    private StackCustomSplitWindow? _window;

    public StackCustomSplitBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _owner = owner;
        _entManager = IoCManager.Resolve<IEntityManager>();
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<StackCustomSplitWindow>();

        if (_entManager.TryGetComponent<StackComponent>(_owner, out var comp))
            _window.SetMax(comp.Count);

        _window.ApplyButton.OnPressed += _ =>
        {
            if (int.TryParse(_window.AmountLineEdit.Text, out var i))
            {
                SendMessage(new StackCustomSplitAmountMessage(i));
                _window.Close();
            }
        };
    }
}
