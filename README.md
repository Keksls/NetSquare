# NetSquare

NetSquare is a lightweight C# networking library for client/server applications that need simple TCP messaging, optional UDP datagrams, request/reply callbacks, message dispatching, and basic world synchronization helpers. It ships as multi-target NuGet packages for Unity-compatible projects and standalone C# applications on Windows, Linux, and macOS.

## Packages

Install the package that matches the side of your application:

```powershell
NuGet\Install-Package NetSquare.Server -Version 1.1.0
NuGet\Install-Package NetSquare.Client -Version 1.1.0
```

or with the .NET CLI:

```bash
dotnet add package NetSquare.Server --version 1.1.0
dotnet add package NetSquare.Client --version 1.1.0
```

`NetSquare.Server` includes the server library and `NetSquareCore.dll`.
`NetSquare.Client` includes the client library and `NetSquareCore.dll`.

## Target Frameworks

The packages include assemblies for:

- `net48`: legacy .NET Framework 4.8 desktop and older Windows projects.
- `netstandard2.0`: broad compatibility target, including many Unity and library scenarios.
- `net8.0`: current LTS standalone .NET applications.
- `net10.0`: latest LTS standalone .NET applications.

Use the modern .NET targets for new standalone servers and tools when possible. They benefit from newer runtime socket, threading, GC, and async I/O improvements. Use `netstandard2.0` when you need maximum Unity/library compatibility.

## Main Concepts

- `NetSquareServer` listens for clients, assigns client IDs, dispatches messages, manages connected clients, and can host worlds.
- `NetSquareClient` connects to a server, sends TCP or UDP messages, dispatches incoming messages, and can join worlds.
- `NetworkMessage` is the message container. It stores a `HeadID`, the sender `ClientID`, a message type, optional reply ID, and serialized payload.
- `NetSquareDispatcher` maps message `HeadID` values to callbacks.
- `NetSquareActionAttribute` can auto-bind public static methods to message IDs.
- `WorldsManager` and `NetsquareTransformFrame` provide basic world membership and transform synchronization helpers.

## Shared Message IDs

Use a shared enum in both client and server projects so both sides agree on message IDs.

```csharp
public enum GameMessage : ushort
{
    Chat = 1,
    Ping = 2,
    Welcome = 3
}
```

## Server Quick Start

```csharp
using System;
using NetSquare.Core;
using NetSquare.Server;

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

        server.Worlds.AddWorld(1, "Lobby", 128);
        server.Start(port: 5555, allowLocalIP: true, bindDispatcher: false, CheckBlackList: true);

        Console.WriteLine("Server running. Press Enter to stop.");
        Console.ReadLine();
        server.Stop();
    }
}
```

## Client Quick Start

```csharp
using System;
using NetSquare.Client;
using NetSquare.Core;

public static class Program
{
    public static void Main()
    {
        NetSquareClient client = new NetSquareClient(autoBindNetsquareActions: false);

        client.OnConnected += clientID =>
        {
            Console.WriteLine("Connected as " + clientID);
            client.SendMessage(new NetworkMessage(GameMessage.Chat).Set("Hello from the client"));

            client.SendMessage(new NetworkMessage(GameMessage.Ping), reply =>
            {
                Console.WriteLine("Server replied: " + reply.Serializer.GetString());
            });
        };

        client.OnDisconected += () => Console.WriteLine("Disconnected");
        client.OnConnectionFail += () => Console.WriteLine("Connection failed");

        client.Dispatcher.AddHeadAction(GameMessage.Welcome, "Welcome", message =>
        {
            string text = message.Serializer.GetString();
            uint assignedID = message.Serializer.GetUInt();
            Console.WriteLine(text + " - assigned ID: " + assignedID);
        });

        client.Dispatcher.AddHeadAction(GameMessage.Chat, "Chat", message =>
        {
            uint senderID = message.Serializer.GetUInt();
            string text = message.Serializer.GetString();
            Console.WriteLine(senderID + ": " + text);
        });

        client.Connect("127.0.0.1", 5555, NetSquareProtocoleType.TCP_AND_UDP);

        Console.WriteLine("Press Enter to disconnect.");
        Console.ReadLine();
        client.Disconnect();
    }
}
```

## NetworkMessage Payloads

Write values in order with `Set(...)` and read them back in the same order through `message.Serializer`.

```csharp
NetworkMessage outgoing = new NetworkMessage(GameMessage.Chat)
    .Set((uint)42)
    .Set("hello")
    .Set(123.45f)
    .Set(true);

uint id = message.Serializer.GetUInt();
string text = message.Serializer.GetString();
float amount = message.Serializer.GetFloat();
bool enabled = message.Serializer.GetBool();
```

Supported payload helpers include primitive values, strings, byte arrays, numeric arrays, `INetSquareSerializable` objects, lists, and dictionaries.

## Dispatching

You can register callbacks manually:

```csharp
server.Dispatcher.AddHeadAction(GameMessage.Chat, "Chat", OnChat);

private static void OnChat(NetworkMessage message)
{
    string text = message.Serializer.GetString();
}
```

You can also auto-bind public static methods with `NetSquareActionAttribute`:

```csharp
using NetSquare.Core;

public static class NetworkHandlers
{
    [NetSquareAction(GameMessage.Chat)]
    public static void OnChat(NetworkMessage message)
    {
        string text = message.Serializer.GetString();
    }
}
```

If you use auto-binding, construct `NetSquareClient` with `autoBindNetsquareActions: true` or start the server with `bindDispatcher: true`.

## Request and Reply

Clients can send a message and receive a matching reply callback.

```csharp
client.SendMessage(new NetworkMessage(GameMessage.Ping).Set("ping"), reply =>
{
    string response = reply.Serializer.GetString();
    Console.WriteLine(response);
});
```

The server replies to the original message:

```csharp
server.Dispatcher.AddHeadAction(GameMessage.Ping, "Ping", message =>
{
    server.Reply(message, new NetworkMessage().Set("pong"));
});
```

## TCP and UDP

Use TCP for reliable ordered messages:

```csharp
client.SendMessage(new NetworkMessage(GameMessage.Chat).Set("reliable"));
server.SendToClient(new NetworkMessage(GameMessage.Chat).Set("from server"), clientID);
```

Use UDP for latency-sensitive data where occasional packet loss is acceptable:

```csharp
client.SendMessageUDP(new NetworkMessage(GameMessage.Chat).Set("unreliable update"));
server.SendToClientUDP(new NetworkMessage(GameMessage.Chat).Set("unreliable update"), clientID);
```

When a client connects with `NetSquareProtocoleType.TCP_AND_UDP`, NetSquare performs the TCP handshake and enables UDP messaging for that connection.

## World Synchronization

The server can host worlds:

```csharp
NetSquareServer server = new NetSquareServer(NetSquareProtocoleType.TCP_AND_UDP, useWorldManager: true);
server.Worlds.AddWorld(1, "Lobby", 128);
```

The client can join a world and send transform frames:

```csharp
client.WorldsManager.TryJoinWorld(1, new NetsquareTransformFrame(0, 0, 0), joined =>
{
    Console.WriteLine("Joined world: " + joined);
});

client.WorldsManager.OnReceiveSynchFrames += (clientID, frames) =>
{
    foreach (INetSquareSynchFrame frame in frames)
        Console.WriteLine("Frame from " + clientID + ": " + frame.SynchFrameType);
};

client.WorldsManager.SendSynchFrame(
    new NetsquareTransformFrame(_x: 10, _y: 0, _z: 5, _time: 1.25f));
```

Use `WorldsManager.SynchronizationTransport` or `SynchronizeUsingUDP` to choose whether world synchronization frames are sent through reliable TCP or unreliable UDP.

## Threading

Dispatcher callbacks may be invoked from networking or queue worker threads. In UI or Unity projects, marshal callbacks to the main thread:

```csharp
using System;
using System.Collections.Concurrent;

ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

client.Dispatcher.SetMainThreadCallback((action, message) =>
{
    mainThreadQueue.Enqueue(() => action(message));
});

// Call this from your UI loop or Unity Update method.
while (mainThreadQueue.TryDequeue(out Action callback))
{
    callback();
}
```

## Server Configuration

`NetSquareConfigurationManager` reads and writes `config.json` in the current working directory. Defaults are created automatically when no config file exists.

```csharp
NetSquareConfiguration config = NetSquareConfigurationManager.Configuration;
config.Port = 5555;
config.NbQueueThreads = 2;
config.NbSendingThreads = 1;
config.ReceivingBufferSize = 4096;
config.UpdateFrequencyHz = 30;
config.LockConsole = false;
config.BlackListFilePath = "[current]/BlackListedIP.json";

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

## License

MIT
