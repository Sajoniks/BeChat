using System.Collections.Immutable;
using BeChat.Client.ConsoleUtility;
using BeChat.Common.Protocol;
using BeChat.Common.Protocol.V1;
using BeChat.Relay;

namespace BeChat.Client.View;

public class LoginView : View
{
    private readonly ConsoleSelector _loginOrRegister;

    private readonly ConsolePrompt _loginPrompt;
    private readonly ConsolePrompt _passPrompt;
    private readonly ConsolePrompt _passPrompt2;
    private int _focused = 0;
    
    private enum State
    {
        Select,
        Login,
        Register
    }

    private State _curState = State.Select;
    
    public LoginView(Window w) : base(w)
    {
        _loginOrRegister = new ConsoleSelector("Welcome to BeChat", new string[] { "Login", "Register" });
        _loginOrRegister.Prompted += LoginOrRegisterOnPrompted;
        
        var usernamePromptSettings = new ConsolePrompt.Settings
        {
            ValidInput = "0123456789abcdefghijklmnopqrstuvwxyz",
            Validator = (str) => str.Length >= 3 && str.Length <= 12,
            Modifier = Char.ToLower
        };
        var passwordPromptSettings = new ConsolePrompt.Settings
        {
            ValidInput = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!@$*()_-+=[]{}?"
        };

        _loginPrompt = new ConsolePrompt("Username", usernamePromptSettings);
        _passPrompt = new ConsolePrompt("Password", passwordPromptSettings);
        _passPrompt2 = new ConsolePrompt("Repeat password", passwordPromptSettings);
    }

    private void WriteTokenToFile(string token)
    {
        string tokenFileName = "token.tok";
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp");
        string tokenPath = Path.Combine(directory, tokenFileName);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(tokenPath, token);
    }

    private string ReadTokenFromFile()
    {
        string tokenFileName = "token.tok";
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp");
        string tokenPath = Path.Combine(directory, tokenFileName);

        if (!File.Exists(tokenPath))
        {
            return "";
        }

        return File.ReadAllText(tokenPath);
    }

    private void DeleteTokenFile()
    {
        string tokenFileName = "token.tok";
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "tmp");
        string tokenPath = Path.Combine(directory, tokenFileName);

        if (File.Exists(tokenPath))
        {
            File.Delete(tokenPath);
        }
    }
    
    private void LoginOrRegisterOnPrompted(object? sender, ConsoleSelector.Result e)
    {
        _loginOrRegister.Close();
        
        if (e.Index == 0)
        {
            _curState = State.Login;
            
            _loginPrompt.Draw();
            _passPrompt.Draw();
        }
        else
        {
            _curState = State.Register;
            
            _loginPrompt.Draw();
            _passPrompt.Draw();
            _passPrompt2.Draw();
        }
        
        _loginPrompt.Focus();
        _focused = 0;
    }

    public override void OnShow()
    {
        if (_curState == State.Select)
        {
            bool autoLogin = false;
            string token = ReadTokenFromFile();
            try
            {
                RelayConnection conn = Parent.App.Connection!;
                conn.SendAsync(new NetMessageAutoLogin()
                {
                    Token = token
                }).GetAwaiter().GetResult();

                Response response = conn.ReceiveAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (!response.IsError)
                {
                    var ct = response.ReadContent<NetContentLoginRegister>();
                    WriteTokenToFile(ct.Token);
                    
                    Parent.App.Authorization.SetUser(new BeChatUser(
                        id: Guid.Empty, 
                        userName: ct.UserName,
                        token: ct.Token,
                        online: true
                    ));
                    
                    autoLogin = true;
                }
            }
            catch (Exception)
            {
                autoLogin = false;
            }

            if (!autoLogin)
            {
                _loginOrRegister.Draw();
            }
            else
            {
                Parent.SetView(Parent.MainMenuView, deleteCurrent: true);
            }
        }
        else if (_curState == State.Login)
        {
            _loginPrompt.Draw();
            _passPrompt.Draw();
            
            _loginPrompt.Focus();
            _focused = 0;
        }
        else
        {
            _loginPrompt.Draw();
            _passPrompt.Draw();
            _passPrompt2.Draw();
            
            _loginPrompt.Focus();
            _focused = 0;
        }
    }

    public override void OnClose()
    {
        _loginOrRegister.Close();
        _passPrompt2.Close();
        _passPrompt.Close();
        _loginPrompt.Close();
    }

    public override bool OnKeyboardInput(ConsoleKeyInfo keyInfo)
    {
        if (_curState == State.Select)
        {
            _loginOrRegister.ConsoleInput(keyInfo);
        }
        else
        {
            int max = (_curState == State.Login) ? 1 : 2;

            bool tryPrompt = false;
            switch (keyInfo.Key)
            {
                case ConsoleKey.UpArrow:
                    _focused = Math.Max(0, _focused - 1);
                    break;
                
                case ConsoleKey.DownArrow:
                    _focused = Math.Min(_focused + 1, max);
                    break;
                
                case ConsoleKey.Enter:
                    tryPrompt = true;
                    break;
            }

            if (!tryPrompt)
            {
                _loginPrompt.Unfocus();
                _passPrompt.Unfocus();
                _passPrompt2.Unfocus();
                
                ConsolePrompt active;
                switch (_focused)
                {
                    case 0:
                        active = _loginPrompt;
                        break;

                    case 1:
                        active = _passPrompt;
                        break;

                    case 2:
                        active = _passPrompt2;
                        break;

                    default:
                        throw new InvalidProgramException("Invalid widget");
                }
                
                active.ConsoleInput(keyInfo);
            }
            else
            {
                string login = _loginPrompt.CopyString();
                string pass = _passPrompt.CopyString();
                string pass2 = _passPrompt2.CopyString();

                if (login.Length == 0 || pass.Length == 0)
                {
                    Parent.ShowError("Username or Password must not be empty");
                }
                else if (_curState == State.Register && !pass.Equals(pass2))
                {
                    Parent.ShowError("Passwords does not match");
                }
                else
                {

                    try
                    {
                        // @todo loading
                        
                        RelayConnection conn = Parent.App.Connection!;
                        
                        if (_curState == State.Login)
                        {
                            conn.SendAsync(new NetMessageLogin
                            {
                                UserName = login,
                                Password = pass
                            }).GetAwaiter().GetResult();
                        }
                        else
                        {
                            conn.SendAsync(new NetMessageRegister()
                            {
                                UserName = login,
                                Password = pass
                            }).GetAwaiter().GetResult();
                        }

                        Response response = conn.ReceiveAsync().GetAwaiter().GetResult();
                        if (response.IsError)
                        {
                            Parent.ShowError(response.ReadError().Message);
                        }
                        else
                        {
                            var ct = response.ReadContent<NetContentLoginRegister>();

                            _passPrompt2.Close();
                            _passPrompt.Close();
                            _loginPrompt.Close();

                            if (ConsoleSelector.SelectBool("Remember login?"))
                            {
                                WriteTokenToFile(ct.Token);
                            }

                            Parent.App.Authorization.SetUser(new BeChatUser(
                                id: Guid.Empty, 
                                userName: ct.UserName,
                                token: ct.Token,
                                online: true
                            ));
                            
                            _curState = State.Select;
                            Parent.SetView(Parent.MainMenuView, deleteCurrent: true);
                        }
                    }
                    catch (Exception e)
                    {
                        Parent.ShowError(e.Message);
                    }
                }
            }
        }

        return true;
    }

    public override bool OnKeyboardCancel()
    {
        if (_curState != State.Select)
        {
            _loginPrompt.ClearBuffer();
            _passPrompt.ClearBuffer();
            _passPrompt2.ClearBuffer();
            
            _passPrompt2.Close();
            _passPrompt.Close();
            _loginPrompt.Close();

            _curState = State.Select;
            
            _loginOrRegister.Draw();
            return true;
        }
        else
        {
            return false;
        }
    }

    public override IEnumerable<View> Views => ImmutableArray<View>.Empty;
}