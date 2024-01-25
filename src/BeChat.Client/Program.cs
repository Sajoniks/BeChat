using BeChat;
using BeChat.Client.ConsoleUtility;
using BeChat.Common.Protocol.V1.Messages;
using BeChat.Logging;
using Microsoft.Extensions.Configuration;

var application = BeChatApplication.Create();
application.ConfigureServices();

IConfiguration configuration = application.Configuration;
ILogger logger = application.Logger;
Bootstrap bootstrap = application.Bootstrap;

var spinner = new AsyncConsoleSpinner();
spinner.Text = "Initializing";
spinner.RunAsync();

await bootstrap.DiscoverAsync();

spinner.Text = "Connecting to relay";

var relayConnection = await application.ConnectToRelayAsync();

spinner.Text = "Connected";
Thread.Sleep(100);

spinner.Dispose();

Console.Clear();
ConsoleHelpers.PrintLogo();
Thread.Sleep(200);

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
    await relayConnection.Send(new LoginRequest(token));
    var response = await relayConnection.Receive<LoginResponse>();

    if (!response.IsError)
    {
        logged = true;
        userName = response.UserName;
    }
}

while(!logged)
{
    string loginOrRegister = ConsoleSelector.Select("Do you want to", new[]
    {
        "Login",
        "Register"
    });

    if (loginOrRegister.Equals("Login"))
    {
        do
        {
            userName = ConsolePrompt.Prompt("Enter username");
        } while (userName.Length < 3 || userName.Length > 10);

        do
        {
            password = ConsolePrompt.Prompt("Enter password");
        } while (password.Length < 3 || password.Length > 10);

        await relayConnection.Send(new LoginRequest(userName, password));
        var response = await relayConnection.Receive<LoginResponse>();

        if (response.IsError)
        {
            Console.WriteLine(response.Error!.Message);
            Thread.Sleep(1500);
            Console.Clear();
            ConsoleHelpers.PrintLogo();
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tokenFilePath)!);
            File.WriteAllText(tokenFilePath, response.Token);
            token = response.Token;
            logged = true;
            break;
        }
    }
    else if (loginOrRegister.Equals("Register"))
    {
        do
        {
            userName = ConsolePrompt.Prompt("Enter username");
        } while (userName.Length < 3 || userName.Length > 10);

        do
        {
            password = ConsolePrompt.Prompt("Enter password");
        } while (password.Length < 3 || password.Length > 10);

        await relayConnection.Send(new RegisterRequest(userName, password));
        var response = await relayConnection.Receive<RegisterResponse>();

        if (response.IsError)
        {
            Console.WriteLine(response.Error!.Message);
            Thread.Sleep(1500);
            Console.Clear();
            ConsoleHelpers.PrintLogo();
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(tokenFilePath)!);
            File.WriteAllText(tokenFilePath, response.Token);
            token = response.Token;
            logged = true;
            break;
        }
    }
}

Console.WriteLine("Welcome, {0}", userName);
string command = "";
do
{
    command = ConsoleSelector.Select("Choose command", new[]
    {
        "Join",
        "Quit"
    });

    switch (command)
    {
        case "Join":
            break;
        
        case "Quit":
            break;
    }
} while (!command.Equals("Quit"));

