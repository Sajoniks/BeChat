using System.Collections.Immutable;
using BeChat.Client.ConsoleUtility;

namespace BeChat.Client.View;

public class ProfileView : View
{
    private readonly ConsoleSelector _opts;
    public ProfileView(Window w) : base(w)
    {
        _opts = new ConsoleSelector(null, new string[]
        {
            "[Profile] Set UserName",
            "[Profile] Delete profile",
            "Logout"
        });
    }

    public override void OnShow()
    {
        _opts.Draw();
    }

    public override void OnClose()
    {
        _opts.Close();
    }

    public override bool OnKeyboardInput(ConsoleKeyInfo keyInfo)
    {
        _opts.ConsoleInput(keyInfo);
        return true;
    }

    public override bool OnKeyboardCancel()
    {
        Parent.NavigateBack();
        return true;
    }

    public override IEnumerable<View> Views => ImmutableArray<View>.Empty;
}