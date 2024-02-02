using System.Collections.Immutable;
using System.Collections.ObjectModel;
using BeChat.Client.ConsoleUtility;

namespace BeChat.Client.View;

public class MainMenuView : View
{
    private readonly ConsoleSelector _selector;
    private readonly ObservableCollection<string> _selectorOpts;

    public MainMenuView(Window parent) : base(parent)
    {
        _selectorOpts = new ObservableCollection<string>
        {
            "[Friends] Add",
            "[Friends] Contact",
            "[Profile] Profile",
            "Notifications",
            "Exit"
        };
        _selector = new ConsoleSelector("", _selectorOpts);
        _selector.Prompted += SelectorOnPrompted;

        parent.App.Invitations.CollectionChanged += (_, e) =>
        {
            int invitations = parent.App.Invitations.Count;
            _selectorOpts[3] = invitations == 0 ? "Notifications" : $"Notifications ({invitations})";
        };
    }

    private void SelectorOnPrompted(object? sender, ConsoleSelector.Result e)
    {
        // Exit must be the last option in the list
        if (e.Index == _selectorOpts.Count - 1)
        {
            Parent.SetView(Parent.ExitView);
        }
        else
        {
            switch (e.Index)
            {
                case 0:
                    Parent.SetView(Parent.AddFriendView);
                    break;
                
                case 1: 
                    Parent.SetView(Parent.FriendListView);
                    break;
                
                case 2:
                    Parent.SetView(Parent.ProfileView);
                    break;
                
                case 3:
                    Parent.SetView(Parent.NotificationsView);
                    break;
            }
        }
    }

    public override void OnShow()
    {
        ConsoleHelpers.PrintLogo();
        _selector.Title = $"Welcome, {Parent.App.Authorization.CurrentUser!.UserName}";
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
        Parent.SetView(Parent.ExitView);
        return Parent.ExitView.OnKeyboardCancel();
    }

    public override IEnumerable<View> Views => ImmutableArray<View>.Empty;
}