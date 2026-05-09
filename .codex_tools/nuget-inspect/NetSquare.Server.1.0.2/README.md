# NetSquare.Server

`NetSquare.Server` is the server-side package for NetSquare. It provides a TCP server with optional UDP messaging, client ID management, message dispatching, request/reply support, broadcast helpers, runtime configuration, and basic world synchronization.

The package targets .NET Framework 4.8 and includes `NetSquare_Server.dll` plus `NetSquareCore.dll`.

## Installation

```powershell
NuGet\Install-Package NetSquare.Server -Version 1.0.2
```

or:

```bash
dotnet add package NetSquare.Server --version 1.0.2
```

## Basic Server

```csharp
using System;
using NetSquare.Core;
using NetSquare.Server;

public enum GameMessage : ushort
{
    Chat = 1,
    Ping = 2,
    Welcome = 3
}

public static class Program
{
    public static void Main()
    {
        NetSquareServer server = new NetSquareServer(NetSquareProtocoleType.TCP_AND_UDP);

        server.OnClientConnected += clientID =>
        {
            Console.WriteLine("Client connected: " + clientID);
            server.SendToClient(
                new NetworkMessage(GameMessage.Welcome).Set("Welcome to NetSquare").Set(clientID),
                clientID);
        };

        server.OnClientDisconnected += clientID =>
        {
            Console.WriteLine("Client disconnected: " + clientID);
        };

        server.Dispatcher.AddHeadAction(GameMessage.Chat, "Chat", message =>
        {
            string text = message.Serializer.GetString();
            Console.WriteLine("Client " + message.ClientID + ": " + text);

            server.Broadcast(
                new NetworkMessage(GameMessage.Chat)
                    .Set(message.ClientID)
                    .Set(text));
        });

        server.Dispatcher.AddHeadAction(GameMessage.Ping, "Ping", message =>
        {
            server.Reply(message, new NetworkMessage().Set("pong"));
        });

        server.Start(port: 5555, allowLocalIP: true, bindDispatcher: false, CheckBlackList: true);

        Console.WriteLine("Server running. Press Enter to stop.");
        Console.ReadLine();
        server.Stop();
    }
}
```

## Network Messages

`NetworkMessage` carries a message ID, sender client ID, type, optional reply ID, and payload. Write values with `Set(...)` and read them back in the same order.

```csharp
NetworkMessage outgoing = new NetworkMessage(GameMessage.Chat)
    .Set((uint)42)
    .Set("hello")
    .Set(123.45f)
    .Set(true);

uint senderID = message.Serializer.GetUInt();
string text = message.Serializer.GetString();
float value = message.Serializer.GetFloat();
bool enabled = message.Serializer.GetBool();
```

Supported helpers include numeric primitives, strings, chars, booleans, byte arrays, numeric arrays, `INetSquareSerializable` objects, lists, and dictionaries.

## Dispatcher

Register handlers manually:

```csharp
server.Dispatcher.AddHeadAction(GameMessage.Chat, "Chat", OnChat);

private static void OnChat(NetworkMessage message)
{
    string text = message.Serializer.GetString();
}
```

Or auto-bind public static methods with `NetSquareActionAttribute`:

```csharp
using NetSquare.Core;

public static class ServerHandlers
{
    [NetSquareAction(GameMessage.Chat)]
    public static void OnChat(NetworkMessage message)
    {
        string text = message.Serializer.GetString();
    }
}
```

Enable auto-binding by starting the server with `bindDispatcher: true`.

## Replies

Use `Reply` to answer a request sent by a client callback overload.

```csharp
server.Dispatcher.AddHeadAction(GameMessage.Ping, "Ping", message =>
{
    string request = message.Serializer.GetString();
    server.Reply(message, new NetworkMessage().Set("pong for " + request));
});
```

## Sending To Clients

Send to one client:

```csharp
server.SendToClient(new NetworkMessage(GameMessage.Chat).Set("private message"), clientID);
```

Send to many clients:

```csharp
server.SendToClients(
    new NetworkMessage(GameMessage.Chat).Set("group message"),
    new uint[] { 1, 2, 3 });
```

Broadcast to all connected clients:

```csharp
server.Broadcast(new NetworkMessage(GameMessage.Chat).Set("server announcement"));
```

Send an unreliable UDP update:

```csharp
server.SendToClientUDP(new NetworkMessage(GameMessage.Chat).Set("udp update"), clientID);
```

## Client Management

Useful APIs:

```csharp
bool connected = server.IsClientConnected(clientID);
ConnectedClient client = server.SafeGetClient(clientID);
server.DisconnectClient(clientID);
server.ReplaceClientID(oldID, newID);
int verifyingClients = server.GetNbVerifyingClients();
```

You can override client ID allocation:

```csharp
uint nextID = 1000;
server.GetNewClientID = () => nextID++;
```

## Server Configuration

`NetSquareConfigurationManager` reads and writes `config.json` in the current working directory. Defaults are created when no file exists.

```csharp
NetSquareConfiguration config = NetSquareConfigurationManager.Configuration;
config.Port = 5555;
config.NbQueueThreads = 2;
config.NbSendingThreads = 1;
config.ReceivingBufferSize = 4096;
config.UpdateFrequencyHz = 30;
config.LockConsole = false;
config.BlackListFilePath = @"[current]\BlackListedIP.json";

NetSquareConfigurationManager.SaveConfiguration(config);
```

Important settings:

- `Port`: default server port.
- `NbQueueThreads`: number of message dispatch worker threads.
- `NbSendingThreads`: number of TCP sending threads.
- `ReceivingBufferSize`: receive buffer size.
- `UpdateFrequencyHz`: server update loop frequency.
- `LockConsole`: disables console quick-edit selection on Windows when enabled.
- `BlackListFilePath`: path for the blacklist file. `[current]` is replaced by the process working directory.

## Worlds

Create worlds on the server:

```csharp
NetSquareServer server = new NetSquareServer(NetSquareProtocoleType.TCP_AND_UDP, useWorldManager: true);

server.Worlds.AddWorld(1, "Lobby", 128);
server.Worlds.AddWorld(2, "Arena", 32);
```

Inspect world membership:

```csharp
bool inWorld = server.Worlds.IsInWorld(clientID);
ushort worldID = server.Worlds.GetClientWorldID(clientID);
```

Listen for world events:

```csharp
server.Worlds.OnClientJoinWorld += (worldID, clientID, transform, message) =>
{
    Console.WriteLine("Client " + clientID + " joined world " + worldID + " at " + transform);
};

server.Worlds.OnClientMove += (clientID, transform) =>
{
    Console.WriteLine("Client " + clientID + " moved to " + transform);
};
```

Broadcast inside a world through the world manager when the sender is already in a world:

```csharp
server.Worlds.BroadcastToWorld(
    new NetworkMessage(GameMessage.Chat, clientID).Set("message for this world"));
```

## Threading

The server receives network messages, queues them, and dispatches them through queue worker threads. Keep handlers fast. For shared game state or UI-bound state, protect access with locks, queues, or your engine's main-thread dispatcher.

## Shutdown

Call `Stop()` for a clean shutdown. The server sends disconnect notices, stops listeners, stops message queues, disconnects clients, and clears listener state.

```csharp
server.Stop();
```

## License

MIT
