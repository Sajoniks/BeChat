using System.Net;

namespace BeChat.Relay.Entites;

public record RequestConnectDto(Guid initiatorId, IPEndPoint privateEp, IPEndPoint publicEp);