using System.Collections.Immutable;
using System.Collections.Specialized;
using BeChat.Client.ConsoleUtility;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;
using BeChat.Relay;

namespace BeChat.Client.View;

public class NotificationsView : View
{
    private readonly ConsoleSelector _notifications;
    private readonly ConsoleSelector _empty;
    
    public NotificationsView(Window w) : base(w)
    {
        _notifications = new ConsoleSelector("");
        _empty = new ConsoleSelector("You don't have notifications", new[] { "OK" });
        
        Parent.App.Invitations.CollectionChanged += InvitationsOnCollectionChanged;
        
        _notifications.Prompted += NotificationsOnPrompted;
        _empty.Prompted += EmptyOnPrompted;
    }

    private void EmptyOnPrompted(object? sender, ConsoleSelector.Result e)
    {
        Parent.NavigateBack();
    }

    private void NotificationsOnPrompted(object? sender, ConsoleSelector.Result e)
    {
        _notifications.Close();
        _empty.Close();

        if (ConsoleSelector.SelectBool($"Accept invitation?"))
        {
            try
            {
                RelayConnection conn = Parent.App.Connection!;
                conn.SendAsync(new NetMessageAcceptContact
                {
                    Token = Parent.App.Authorization.CurrentUser!.Token,
                    UserId = Parent.App.Invitations[e.Index].Data.UserId
                }).GetAwaiter().GetResult();

                Response response = conn.ReceiveAsync().GetAwaiter().GetResult();
                if (response.IsError)
                {
                    Parent.ShowError(response.ReadError().Message);
                }
                else
                {
                    ConsoleSelector.Select("Request accepted", new[] { "OK" });
                }
            }
            catch (Exception ex)
            {
                Parent.ShowError(ex.Message);
            }
        }

        if (IsVisible)
        {
            if (_notifications.Items.Count > 0)
            {
                _notifications.Draw();
            }
            else
            {
                _empty.Draw();
            }
        }
    }

    private void InvitationsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Parent.EnqueueTask(() =>
        {
            _notifications.Close();
            _notifications.Items.Clear();
            foreach (var invitation in Parent.App.Invitations)
            {
                _notifications.Items.Add($"Friend request from {invitation.Data.UserName}");
            }

            if (IsVisible)
            {
                if (_notifications.Items.Count > 0)
                {
                    _notifications.Title = $"You have {_notifications.Items.Count} notifications";
                    _empty.Close();
                    _notifications.Draw();
                }
                else
                {
                    _empty.Draw();
                }
            }
        });
    }

    public override void OnShow()
    {
        if (_notifications.Items.Count == 0)
        {
            _empty.Draw();
        }
        else
        {
            _notifications.Draw();
        }
    }

    public override void OnClose()
    {
        _notifications.Close();
        _empty.Close();
    }

    public override bool OnKeyboardInput(ConsoleKeyInfo keyInfo)
    {
        if (_notifications.Items.Count == 0)
        {
            _empty.ConsoleInput(keyInfo);
        }
        else
        {
            _notifications.ConsoleInput(keyInfo);
        }

        return true;
    }

    public override bool OnKeyboardCancel()
    {
        Parent.NavigateBack();
        return true;
    }

    public override IEnumerable<View> Views => ImmutableArray<View>.Empty;
}