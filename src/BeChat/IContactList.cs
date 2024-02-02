using System.Collections.Specialized;

namespace BeChat;

public interface IContactList : INotifyCollectionChanged, IList<IUser>
{
}