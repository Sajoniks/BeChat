namespace BeChat;

public interface IKeyedEntity<T>
{
    T Id { get; }
}

public interface IUser : IKeyedEntity<Guid>
{
    string UserName { get; }
    string Token { get; }
    bool IsOnline { get; set; }
}

public interface IAuthorization
{
    void SetUser(IUser user);
    IUser? CurrentUser { get; }
}