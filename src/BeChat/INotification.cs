using System.Collections.Specialized;

namespace BeChat;



public interface INotification<T> : IKeyedEntity<Guid>
{
    T Data { get; }
}

public interface INotificationList<T> : INotifyCollectionChanged, IList<INotification<T>>
{
}