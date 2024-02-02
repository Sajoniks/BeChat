using BeChat;
using BeChat.Client.ConsoleUtility;
using BeChat.Client.View;
using BeChat.Common.Protocol.V1;
using BeChat.Logging;
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

var relayConnection = application.CreateRelayConnection();
var window = new Window(application);

AsyncConsoleSpinner? reconnectSpinner = null;

relayConnection.OnReconnected += (_, _) =>
{
    HandleReconnect(ref reconnectSpinner);
    window.SetView(window.LoginView);
};

relayConnection.OnDisconnect += (_, _) =>
{
    window.CloseAll();
    HandleDisconnect(ref reconnectSpinner);
};

await relayConnection.ConnectToRelayAsync(CancellationToken.None);

spinner.Text = "Connected";
Thread.Sleep(500);

spinner.Dispose();

window.SetView(window.LoginView);
window.WaitUntilExit();

application.Dispose();

void HandleReconnect(ref AsyncConsoleSpinner? spinner)
{
    spinner?.Dispose();
}

void HandleDisconnect(ref AsyncConsoleSpinner? spinner)
{
    spinner = new AsyncConsoleSpinner();
    spinner = new AsyncConsoleSpinner();
    spinner.Text = "Connecting to relay";
    spinner.SpinAsync();
}