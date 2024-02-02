using System.Collections.Immutable;
using System.Collections.ObjectModel;
using BeChat.Client.ConsoleUtility;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;
using BeChat.Relay;

namespace BeChat.Client.View;

public class AddFriendView : View
{
    private ConsoleSelector _searchResultSelector;
    private ConsolePrompt _searchPrompt;
    private Text _text;
    private ObservableCollection<string> _searchResults = new();
    private List<NetMessageContact> _currentSearchList = new();
    private bool _focusSearchResults = false;

    public AddFriendView(Window w) : base(w)
    {
        _searchPrompt = new ConsolePrompt("Find");
        _searchResultSelector = new ConsoleSelector(null, _searchResults);
        _text = new Text("Results empty");
        
        _searchPrompt.Prompted += SearchPromptOnPrompted;
        _searchResultSelector.Prompted += SearchResultSelectorOnPrompted;
    }

    private void SearchResultSelectorOnPrompted(object? sender, ConsoleSelector.Result e)
    {
        _text.Clear();
        _searchResultSelector.Close();
        _searchPrompt.Close();

        if (ConsoleSelector.SelectBool($"Do you want to contact {e.Option}?"))
        {
            bool added = false;
            try
            {
                RelayConnection conn = Parent.App.Connection!;
                conn.SendAsync(new NetMessageAddContact
                {
                    Token = Parent.App.Authorization.CurrentUser!.Token,
                    UserId = _currentSearchList[e.Index].UserId
                }).GetAwaiter().GetResult();

                Response response = conn.ReceiveAsync().GetAwaiter().GetResult();
                if (response.IsError)
                {
                    Parent.ShowError(response.ReadError().Message);
                }
                else
                {
                    added = true;
                }
            }
            catch (Exception ex)
            {
                Parent.ShowError(ex.Message);
            }

            if (added)
            {
                _searchResults.RemoveAt(e.Index);

                _searchPrompt.Draw();
                
                if (_searchResults.Count == 0)
                {
                    _focusSearchResults = false;
                    _searchPrompt.Focus();
                }
                else
                {
                    _focusSearchResults = true;
                    _searchResultSelector.Draw();
                }
            }
        }
        else
        {
            _focusSearchResults = true;
            _searchPrompt.Draw();
            _searchResultSelector.Draw();
        }
    }

    private void SearchPromptOnPrompted(object? sender, ConsolePrompt.Result e)
    {
        try
        {
            _text.Clear();
            _searchResultSelector.Close();
            
            RelayConnection conn = Parent.App.Connection!;
            conn.SendAsync(new NetMessageFindContacts
            {
                QueryString = e.Input,
                Token = Parent.App.Authorization.CurrentUser!.Token
            }).GetAwaiter().GetResult();

            Response response = conn.ReceiveAsync().GetAwaiter().GetResult();
            if (response.IsError)
            {
                Parent.ShowError(response.ReadError().Message);
            }
            else
            {
                _currentSearchList = response.ReadContent<NetResponseContactsList>().Contacts;
                _searchResults.Clear();

                if (_currentSearchList.Count > 0)
                {
                    foreach (NetMessageContact contact in _currentSearchList)
                    {
                        _searchResults.Add(contact.UserName);
                    }

                    _focusSearchResults = true;
                    _searchPrompt.Unfocus();
                    _searchResultSelector.Draw();
                }
                else
                {
                    _text.Draw();
                }
            }
        }
        catch (Exception ex)
        {
            Parent.ShowError(ex.Message);
        }
    }

    public override void OnShow()
    {
        _searchPrompt.Draw();

        if (_focusSearchResults)
        {
            if (_searchResults.Count > 0)
            {
                _searchResultSelector.Draw();
            }
            else
            {
                _focusSearchResults = false;
            }
        }

        if (!_focusSearchResults)
        {
            _searchPrompt.Focus();
        }
    }

    public override void OnClose()
    {
        _searchResultSelector.Close();
        _searchPrompt.Close();
    }

    public override bool OnKeyboardInput(ConsoleKeyInfo keyInfo)
    {
        if (!_focusSearchResults)
        {
            _searchPrompt.ConsoleInput(keyInfo);
        }
        else
        {
            _searchResultSelector.ConsoleInput(keyInfo);
        }
        return true;
    }

    public override bool OnKeyboardCancel()
    {
        if (_focusSearchResults)
        {
            _focusSearchResults = false;
            _searchResultSelector.Close();
            _searchPrompt.Focus();
        }
        else
        {
            Parent.NavigateBack();
        }
        return true;
    }
    
    public override IEnumerable<View> Views => ImmutableArray<View>.Empty;
}