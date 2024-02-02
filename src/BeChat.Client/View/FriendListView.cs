using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Net;
using BeChat.Client.ConsoleUtility;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;
using BeChat.Relay;

namespace BeChat.Client.View;

public class FriendListView : View
{
    private readonly ConsoleSelector _selector;
    private readonly ConsoleSelector _empty;
    private CancellationTokenSource _cts;

    public FriendListView(Window w) : base(w)
    {
        _cts = new CancellationTokenSource();
        _selector = new ConsoleSelector("Contacts");
        _selector.Prompted += SelectorOnPrompted;
        
        _empty = new ConsoleSelector("No contacts", new string[] { "Exit" });
        _empty.Prompted += EmptyOnPrompted;
        
        w.App.ContactList.CollectionChanged += ContactListOnCollectionChanged;
        w.App.UserPresenceChange += AppUserPresenceChange;
    }

    private void AppUserPresenceChange(object? sender, IUser e)
    {
        Parent.EnqueueTask(() =>
        {
            _selector.Items.Clear();
            lock (Parent.App.ContactList)
            {
                foreach (var user in Parent.App.ContactList)
                {
                    if (user.IsOnline)
                    {
                        _selector.Items.Add($"{user.UserName} [online]");
                    }
                    else
                    {
                        _selector.Items.Add(user.UserName);
                    }
                }
            }
        });
    }

    private void EmptyOnPrompted(object? sender, ConsoleSelector.Result e)
    {
        Parent.NavigateBack();
    }

    private void SelectorOnPrompted(object? sender, ConsoleSelector.Result e)
    {
        _selector.Close();
        IUser user = Parent.App.ContactList[e.Index];
        if (ConsoleSelector.SelectBool($"Do you want to contact {user.UserName}?"))
        {
            try
            {
                RelayConnection conn = Parent.App.Connection!;
                conn.SendAsync(new NetMessageConnect
                {
                    Token = Parent.App.Authorization.CurrentUser!.Token,
                    ConnectToId = user.Id,
                    PublicIp  = Parent.App.Bootstrap.PublicEndPoints.First(),
                    PrivateIp = new IPEndPoint( Parent.App.Bootstrap.PrivateIps.First(), Parent.App.Bootstrap.PublicEndPoints.First().Port )
                }).GetAwaiter().GetResult();

                Response response = conn.ReceiveAsync().GetAwaiter().GetResult();
                if (response.IsError)
                {
                    Parent.ShowError(response.ReadError().Message);
                }
                else
                {
                    ((ChatView)Parent.ChatView).SetWaitConnection();
                    Parent.SetView(Parent.ChatView, deleteCurrent: true);
                }
            }
            catch (Exception ex)
            {
                Parent.ShowError(ex.Message);
            }
        }

        if (IsVisible)
        {
            _selector.Draw();
        }
    }

    private void ContactListOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Parent.EnqueueTask(() =>
        {
            _selector.Items.Clear();
            lock (Parent.App.ContactList)
            {
                foreach (var user in Parent.App.ContactList)
                {
                    if (user.IsOnline)
                    {
                        _selector.Items.Add($"{user.UserName} [online]");
                    }
                    else
                    {
                        _selector.Items.Add(user.UserName);
                    }
                }
            }

            if (IsVisible)
            {
                if (_selector.Items.Count > 0)
                {
                    _empty.Close();
                    _selector.Draw();
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
        _cts.TryReset();
        
        int receivedContactLength = _selector.Items.Count;
        if (receivedContactLength == 0)
        {
            _empty.Draw();
        }
        else
        {
            try
            {
             
            }
            catch (Exception)
            {
                _cts.Cancel();
            }

            _selector.Draw();
        }
    }

    public override void OnClose()
    {
        _empty.Close();
        _selector.Close();
        
        _cts.Cancel();
    }

    public override bool OnKeyboardInput(ConsoleKeyInfo keyInfo)
    {
        if (_selector.Items.Count == 0)
        {
            _empty.ConsoleInput(keyInfo);
        }
        else
        {
            _selector.ConsoleInput(keyInfo);
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