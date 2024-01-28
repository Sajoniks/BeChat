using System.Collections.ObjectModel;
using System.Media;
using System.Runtime.InteropServices;
using BeChat;
using BeChat.Client.ConsoleUtility;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;
using BeChat.Logging;
using BeChat.Relay;
using Microsoft.Extensions.Configuration;

var application = BeChatApplication.Create();
application.ConfigureServices();

IConfiguration configuration = application.Configuration;
ILogger logger = application.Logger;
Bootstrap bootstrap = application.Bootstrap;

var spinner = new AsyncConsoleSpinner();
spinner.Text = "Initializing";
spinner.SpinAsync();

await bootstrap.DiscoverAsync();

spinner.Text = "Connecting to relay";

var notify = new NetMessageNotify();
var relayConnection = await application.ConnectToRelayAsync();
relayConnection.AddListener(NetMessage<NetNotifyNewFriend>.GetMessageId(), notify);
relayConnection.AddListener(NetMessage<NetNotifyNewInvitation>.GetMessageId(), notify);

var commands = new ObservableCollection<string>(new []
{
    "[Friends] Add",
    "[Friends] Contact",
    "[Profile] Profile",
    "Notifications",
    "Exit"
});
notify.Invitations.CollectionChanged += (_, args) =>
{
    PlaySystemSound(SystemSounds.Beep);
    int count = args.NewItems?.Count ?? 0;
    commands[3] = count == 0 ? "Notifications" : $"Notifications ({count})";
};


string userName = "";
string token = "";
string password = "";

string tokenFilePath = Path.Combine("tmp", "token.dat");
if (File.Exists(tokenFilePath))
{
    token = File.ReadAllText(Path.Combine("tmp", "token.dat"));
}

bool logged = false;
if (token.Length != 0)
{
    await relayConnection.SendAsync(new NetMessageAutoLogin
    {
        Token = token
    });
    var response = await relayConnection.ReceiveAsync();

    if (!response.IsError)
    {
        var data = response.ReadContent<NetMessageUserData>();
        logged = true;
        userName = data.UserName;
    }
}

spinner.Text = "Connected";
Thread.Sleep(500);

spinner.Dispose();

Console.Clear();
ConsoleHelpers.PrintLogo();
Thread.Sleep(200);

var usernamePromptSettings = new ConsolePrompt.Settings
{
    ValidInput = "0123456789abcdefghijklmnopqrstuvwxyz",
    Validator = (str) => str.Length >= 3 && str.Length <= 12,
    Modifier = Char.ToLower
};


while(!logged)
{
    var passwordPromptSettings = new ConsolePrompt.Settings
    {
        ValidInput = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!@$*()_-+=[]{}?"
    };
    
    
    int loginOrRegister = ConsoleSelector.Select($"Welcome to BeChat (v. {application.Version})", new[]
    {
        "Login",
        "Register"
    });

    if (loginOrRegister == 0)
    {
        var r1 = ConsolePrompt.Prompt("Enter username", usernamePromptSettings);
        var r2 = ConsolePrompt.Prompt("Enter password", passwordPromptSettings);

        userName = r1.Input;
        password = r2.Input;

        using var loginSpinner = new AsyncConsoleSpinner();
        loginSpinner.Text = "Logging in";
        loginSpinner.SpinAsync();
        
        await relayConnection.SendAsync(new NetMessageLogin()
        {
            UserName = userName,
            Password = password
        });

        var response = await relayConnection.ReceiveAsync();
        
        loginSpinner.Dispose();

        if (response.IsError)
        {
            PlaySystemSound(SystemSounds.Exclamation);

            var error = response.ReadError();
            Console.WriteLine(error.Message);
            Thread.Sleep(1500);
            Console.Clear();
            ConsoleHelpers.PrintLogo();
        }
        else
        {
            var data = response.ReadContent<NetMessageUserData>();

            if (ConsoleSelector.SelectBool("Remember me?"))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(tokenFilePath)!);
                File.WriteAllText(tokenFilePath, data.Token);
            }

            token = data.Token;
            logged = true;
            break;
        }
    }
    else if (loginOrRegister == 1)
    {
        var r1 = ConsolePrompt.Prompt("Enter username", usernamePromptSettings);
        userName = r1.Input;
        
        do
        {
            var r2 = ConsolePrompt.Prompt("Enter password", passwordPromptSettings);
            var r3 = ConsolePrompt.Prompt("Confirm password", passwordPromptSettings);
            
            if (!r3.Input.Equals(r2.Input))
            {
                PlaySystemSound(SystemSounds.Exclamation);

                Console.WriteLine("Passwords do not match");
                Thread.Sleep(1000);
            }
            else
            {
                password = r3.Input;
                break;
            }

        } while (true);

        using var registerSpinner = new AsyncConsoleSpinner();
        registerSpinner.Text = "Registering";
        registerSpinner.SpinAsync();

        await relayConnection.SendAsync(new NetMessageRegister
        {
            UserName = userName,
            Password = password
        });
        var response = await relayConnection.ReceiveAsync();
        
        registerSpinner.Dispose();

        if (response.IsError)
        {
            PlaySystemSound(SystemSounds.Exclamation);

            var error = response.ReadError();
            Console.WriteLine(error.Message);
            Thread.Sleep(1500);
            Console.Clear();
            ConsoleHelpers.PrintLogo();
        }
        else
        {
            var data = response.ReadContent<NetMessageUserData>();
            Directory.CreateDirectory(Path.GetDirectoryName(tokenFilePath)!);
            File.WriteAllText(tokenFilePath, data.Token);
            token = data.Token;
            logged = true;
            break;
        }
    }
}

Console.WriteLine("Welcome, {0}", userName);
int command;

do
{
    if (Console.CursorTop == 0)
    {
        ConsoleHelpers.PrintLogo();
    }
    
    command = ConsoleSelector.Select("Choose command", commands);

    switch (command)
    {
        case 0:
            await AddFriend();
            break;
        
        case 1:
            ConnectToFriend();
            break;

        case 2:
            break;
            
        case 3:
            await CheckInvitations();
            break;
        
        case 4:
            if (ConsoleSelector.SelectBool("Do you want to exit BeChat?", 1))
            {
                if (ConsoleSelector.SelectBool("Do you want to logout from profile?", 1))
                {
                    if (Directory.Exists(Path.GetDirectoryName(tokenFilePath)!))
                    {
                        Directory.Delete(Path.GetDirectoryName(tokenFilePath)!, true);
                    }
                }
            }
            else
            {
                command = -1;
            }

            break;
    }
} while (command != commands.Count - 1);

application.Dispose();


void ConnectToFriend()
{
    Console.Clear();

    if (notify.ContactList.Count == 0)
    {
        Console.WriteLine("No active contacts");
        Thread.Sleep(1000);
        Console.Clear();
        return;
    }
    
    var contactList = notify.ContactList.Select(x => x.UserName).Append("  * Exit").ToArray();
    int optionIdx;
    do
    {
        optionIdx = ConsoleSelector.Select("Whom do you want to connect", contactList);
        if (optionIdx < contactList.Length - 1)
        {
            if (ConsoleSelector.SelectBool($"Connect {contactList[optionIdx]}?"))
            {
                
            }
        }
    } while (optionIdx != contactList.Length - 1);
}

async Task CheckInvitations()
{
    Console.Clear();

    if (notify.Invitations.Count == 0)
    {
        Console.WriteLine("No active invitations");
        Thread.Sleep(1000);
        Console.Clear();
        return;
    }

    var currentContactList = new ObservableCollection<string>();
    foreach (var invitation in notify.Invitations)
    {
        currentContactList.Add(invitation.UserName);
    }
    currentContactList.Add("  - Exit");
    int optionIdx;
    do
    {
        optionIdx = ConsoleSelector.Select("You have invitations", currentContactList);
        if (optionIdx < currentContactList.Count - 1)
        {
            if (ConsoleSelector.SelectBool($"Accept invitation from {currentContactList[optionIdx]}?"))
            {
                using (var sendSpinner = new AsyncConsoleSpinner())
                {
                    sendSpinner.SpinAsync("Accepting");
                    
                    await relayConnection.SendAsync(new NetMessageAcceptContact
                    {
                        Token = token,
                        UserId = notify.Invitations[optionIdx].UserId
                    });

                    var acceptResponse = await relayConnection.ReceiveAsync();
                    if (!acceptResponse.IsError)
                    {
                        currentContactList.RemoveAt(optionIdx);
                        
                        Console.WriteLine($"Friend was added");
                        Thread.Sleep(1000);
                        Console.Clear();
                    }
                    else
                    {
                        PlaySystemSound(SystemSounds.Exclamation);
                        
                        Console.WriteLine(acceptResponse.ReadError().Message);
                        Thread.Sleep(1000);
                        Console.Clear();
                    }
                }
            }
        }
    } while (optionIdx != currentContactList.Count - 1);
}

async Task AddFriend()
{
    Console.Clear();

    do
    {
        var friendNamePrompt = new ConsolePrompt.Settings
        {
            Modifier = usernamePromptSettings.Modifier,
            Validator = (str) => str.Length >= 3 && str.Length <= 12 && !str.Equals(userName),
            ValidInput = usernamePromptSettings.ValidInput,
            InterceptSigsev = true
        };

        ObservableCollection<string> currentFoundContacts = new();

        ConsolePrompt.Result r = ConsolePrompt.Prompt("Whom do you want to add to contact list?", friendNamePrompt);
        if (r.Intercept)
        {
            return;
        }

        string friendName = r.Input;
        
        var searchFriendsSpinner = new AsyncConsoleSpinner();
        searchFriendsSpinner.Text = "Searching";
        searchFriendsSpinner.SpinAsync();

        await relayConnection.SendAsync(new NetMessageFindContacts
        {
            QueryString = friendName,
            Token = token
        });

        var response = await relayConnection.ReceiveAsync();
        
        searchFriendsSpinner.Dispose();
        if (response.IsError)
        {
            Console.WriteLine("Failed to find contacts. Try again later");
            await Task.Delay(1000);
            Console.Clear();
        }
        else
        {
            var searchResult = response.ReadContent<NetMessageFindContactsList>();
            currentFoundContacts.Clear();
            foreach (var ct in searchResult.Contacts)
            {   
                currentFoundContacts.Add(ct.UserName);
            }
            currentFoundContacts.Add("  - Exit");

            HashSet<Guid> sentRequests = new HashSet<Guid>();
            int optionIndex;
            int lastOptionIndex = currentFoundContacts.Count - 1;
            
            do
            {
                optionIndex = ConsoleSelector.Select($"Found {searchResult.Contacts.Count}: ", currentFoundContacts);
                if (optionIndex < lastOptionIndex)
                {
                    if (sentRequests.Contains(searchResult.Contacts[optionIndex].UserId))
                    {
                        continue;
                    }
                    
                    if (ConsoleSelector.SelectBool($"Do you want to contact \"{currentFoundContacts[optionIndex]}\"?"))
                    {
                        Response sendResponse;
                        using (var sendSpinner = new AsyncConsoleSpinner())
                        {
                            sendSpinner.SpinAsync("Sending");

                            await relayConnection.SendAsync(new NetMessageAddContact
                            {
                                Token = token,
                                UserId = searchResult.Contacts[optionIndex].UserId
                            });

                            sendResponse = await relayConnection.ReceiveAsync();
                        }

                        if (!sendResponse.IsError)
                        {
                            Console.WriteLine($"Request was sent to {searchResult.Contacts[optionIndex].UserName}");
                            sentRequests.Add(searchResult.Contacts[optionIndex].UserId);
                            Thread.Sleep(1000);
                            Console.Clear();
                        }
                        else
                        {
                            Console.WriteLine(sendResponse.ReadError().Message);
                            Thread.Sleep(1000);
                            Console.Clear();
                        }
                    }
                }

            } while (optionIndex != currentFoundContacts.Count - 1);
        }

    } while (true);
}

void PlaySystemSound(SystemSound sound)
{
    sound.Play();
}

class NetMessageNotify : IRelayMessageNotify
{
    private ObservableCollection<NetMessageContact> _invitations = new();
    private ObservableCollection<NetMessageContact> _contacts = new();

    public ObservableCollection<NetMessageContact> Invitations => _invitations;
    public ObservableCollection<NetMessageContact> ContactList => _contacts;

    
    [RelayMessageHandler(typeof(NetNotifyNewFriend))]
    public void ReceiveNewFriend(NetNotifyNewFriend friend)
    {
        for (int i = 0; i < _invitations.Count; ++i)
        {
            if (_invitations[i].UserId.Equals(friend.UserId))
            {
                _invitations.RemoveAt(i);
                break;
            }
        }
        _contacts.Add(friend);
    }

    [RelayMessageHandler(typeof(NetNotifyNewInvitation))]
    public void ReceiveNewInvitation(NetNotifyNewInvitation invitation)
    {
        _invitations.Add( invitation );
    }

    
    public void ReceiveRelayMessage(Response response)
    { }
}