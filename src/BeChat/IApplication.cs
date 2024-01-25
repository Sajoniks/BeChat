using Microsoft.Extensions.DependencyInjection;

namespace BeChat;

public interface IApplication
{
    public IServiceProvider Services { get; }
    public void ConfigureServices(Action<IServiceCollection> action);
}