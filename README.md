# NetSquare

NetSquare is a C# client/server networking library for applications and real-time games. It combines reliable TCP messaging, optional authenticated UDP, request/reply callbacks, typed connection feedback, JSON configuration, time synchronization, and world spatialization behind a small API.

## Packages

Install the package that matches the process you are building:

| Package | Purpose | Target frameworks |
| --- | --- | --- |
| `NetSquare.Client` | Client connections, messaging, time sync, and world sync | .NET Standard 2.0, .NET 8, .NET Framework 4.8 |
| `NetSquare.Server` | Server listener, dispatching, worlds, blacklist, and spatialization | .NET Standard 2.0, .NET 8 for Windows, .NET Framework 4.8 |
| `NetSquare.Core` | Shared protocol, serialization, and message contracts | .NET Standard 2.0, .NET 8, .NET Framework 4.8 |

`NetSquare.Client` and `NetSquare.Server` install the matching `NetSquare.Core` version automatically. All three packages must use the same version because the current handshake requires exact Core version equality.

```bash
dotnet add package NetSquare.Client --version 1.0.18
dotnet add package NetSquare.Server --version 1.0.18
```

## Quick start

Define message identifiers shared by the client and server:

```csharp
public enum GameMessage : ushort
{
    Chat = 1,
    Welcome = 2
}
```

Create and start the server:

```csharp
using NetSquare.Core;
using NetSquare.Server;

NetSquareConfigurationManager.Initialize<NetSquareConfiguration>();

NetSquareServer server = new NetSquareServer(NetSquareProtocoleType.TCP_AND_UDP);

server.OnClientConnected += clientID =>
{
    server.SendToClient(
        new NetworkMessage(GameMessage.Welcome)
            .Set("Welcome")
            .Set(clientID),
        clientID);
};

server.Dispatcher.AddHeadAction(GameMessage.Chat, "Chat", message =>
{
    string text = message.Serializer.GetString();
    server.Broadcast(
        new NetworkMessage(GameMessage.Chat)
            .Set(message.ClientID)
            .Set(text));
});

server.Start(port: 5555);
```

Connect a client:

```csharp
using NetSquare.Client;
using NetSquare.Core;

NetSquareClient client = new NetSquareClient(autoBindNetsquareActions: false);

client.OnConnected += clientID =>
{
    client.SendMessage(
        new NetworkMessage(GameMessage.Chat).Set("Hello server"));
};

client.Dispatcher.AddHeadAction(GameMessage.Welcome, "Welcome", message =>
{
    string text = message.Serializer.GetString();
    uint clientID = message.Serializer.GetUInt();
    Console.WriteLine(text + " - client " + clientID);
});

client.Dispatcher.AddHeadAction(GameMessage.Chat, "Chat", message =>
{
    uint senderID = message.Serializer.GetUInt();
    string text = message.Serializer.GetString();
    Console.WriteLine(senderID + ": " + text);
});

client.Connect("127.0.0.1", 5555, NetSquareProtocoleType.TCP_AND_UDP);
```

Values must be read in the same order and with the same types used to write them.

## Choose the right transport

- Use TCP for commands, inventory, authentication, chat, and any message that must arrive in order.
- Use UDP for replaceable, time-sensitive state such as frequent positions. UDP messages may be lost, duplicated, or reordered.
- Enabling TCP plus UDP keeps the TCP session as the source of client identity. UDP datagrams are authenticated with per-session keys negotiated during the handshake.
- Enable TLS when server identity, TCP confidentiality, and protection of the negotiated UDP session key are required.

See [HANDSHAKE.md](HANDSHAKE.md) for the connection sequence, TLS setup, authenticated UDP behavior, compatibility rules, and server tuning.

## Configuration

Client and server settings are persisted as strongly typed JSON:

- the client uses `NetSquareClientConfigurationManager` and defaults to `client.config.json`;
- the server uses `NetSquareConfigurationManager` and defaults to `config.json`;
- applications can derive their own configuration type to store project settings beside the NetSquare settings.

Initialize configuration before constructing the client or server. The generated file contains complete defaults and can then be edited or updated through the typed configuration object.

## Documentation

- [Client and server configuration](CONFIGURATION.md)
- [Client guide](NetSquareClient/README.md)
- [Server guide](NetSquareServer/README.md)
- [Core and serialization guide](NetSquareCore/README.md)
- [Handshake, TLS, and authenticated UDP](HANDSHAKE.md)
- [Packaging and publishing](PACKAGING.md)
- [Release history](CHANGELOG.md)

## Threading

Network callbacks and dispatcher handlers can run on background threads. Keep handlers short and marshal UI or game-engine work to the appropriate main thread. Protect shared application state with locks, concurrent collections, or your engine's main-thread queue.

## License

NetSquare is licensed under the MIT License.
