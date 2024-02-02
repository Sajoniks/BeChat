using System.Collections.Immutable;
using BeChat.Client.ConsoleUtility;

namespace BeChat.Client.View;

public class ErrorView : View
{
    private readonly ConsoleSelector _selector;
    
    public ErrorView(Window w, string errorMessage) : base(w)
    {
        _selector = new ConsoleSelector(errorMessage, new string[] { "OK" });
        _selector.Prompted += SelectorOnPrompted;
    }

    private void SelectorOnPrompted(object? sender, ConsoleSelector.Result e)
    {
        Parent.NavigateBack();
    }

    public override void OnShow()
    {
        _selector.Draw();
    }

    public override void OnClose()
    {
        _selector.Close();
    }

    public override bool OnKeyboardInput(ConsoleKeyInfo keyInfo)
    {
        _selector.ConsoleInput(keyInfo);
        return true;
    }

    public override bool OnKeyboardCancel()
    {
        return true;
    }
    
    public override IEnumerable<View> Views => ImmutableArray<View>.Empty;
}