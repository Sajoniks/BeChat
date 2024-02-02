using Microsoft.Extensions.DependencyInjection;

namespace BeChat;

public interface IApplication
{
    public IAuthorization Authorization { get; }
    public IContactList ContactList { get; }
    public IServiceProvider Services { get; }
    public void ConfigureServices(Action<IServiceCollection> action);
}