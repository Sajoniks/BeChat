using System.Collections.Immutable;
using BeChat.Client.ConsoleUtility;
using BeChat.Common.Protocol;

namespace BeChat.Client.View;

public class ExitView : View
{
    private ConsoleSelector _exitSelect;
    
    public ExitView(Window w) : base(w)
    {
        _exitSelect = new ConsoleSelector("Do you want to exit BeChat?", new string[]
        {
            "Yes", "No"
        }, 1);
        _exitSelect.Prompted += ExitSelectOnPrompted;
    }

    private void ExitSelectOnPrompted(object? sender, ConsoleSelector.Result e)
    {
        if (e.Index == 0)
        {
            Parent.RequestExit();
        }
        else
        {
            Parent.NavigateBack();
        }
    }

    public override void OnShow()
    {
        _exitSelect.Draw();
    }

    public override void OnClose()
    {
        _exitSelect.Close();
    }

    public override bool OnKeyboardInput(ConsoleKeyInfo keyInfo)
    {
        _exitSelect.ConsoleInput(keyInfo);
        return true;
    }

    public override bool OnKeyboardCancel()
    {
        // we will handle this through selector
        return true;
    }
    
    public override IEnumerable<View> Views => ImmutableArray<View>.Empty;
}