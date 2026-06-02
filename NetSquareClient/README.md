# NetSquare.Client

`NetSquare.Client` is the client-side package for NetSquare. It provides a TCP client with optional UDP messaging, request/reply callbacks, dispatcher-based message routing, server time synchronization, and world synchronization helpers.

The package targets .NET Standard 2.0, .NET 8, and .NET Framework 4.8. It includes `NetSquareClient.dll` and depends on `NetSquare.Core`.

## Installation

```powershell
NuGet\Install-Package NetSquare.Client -Version 1.0.7
```

or:

```bash
dotnet add package NetSquare.Client --version 1.0.7
```

## Basic Client

```csharp
using System;
using NetSquare.Client;
using NetSquare.Core;

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
        NetSquareClient client = new NetSquareClient(autoBindNetsquareActions: false);

        client.OnConnected += clientID =>
        {
            Console.WriteLine("Connected as " + clientID);
            client.SendMessage(new NetworkMessage(GameMessage.Chat).Set("Hello server"));
        };

        client.OnDisconected += () => Console.WriteLine("Disconnected");
        client.OnConnectionFail += () => Console.WriteLine("Connection failed");
        client.OnException += ex => Console.WriteLine(ex);

        client.Dispatcher.AddHeadAction(GameMessage.Welcome, "Welcome", message =>
        {
            string text = message.Serializer.GetString();
            uint assignedClientID = message.Serializer.GetUInt();
            Console.WriteLine(text + " - assigned ID: " + assignedClientID);
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

## Sending Messages

Use `NetworkMessage` to write values in the order the receiver will read them.

```csharp
client.SendMessage(
    new NetworkMessage(GameMessage.Chat)
        .Set("hello")
        .Set(123)
        .Set(true));
```

Read values from `message.Serializer` in the same order:

```csharp
string text = message.Serializer.GetString();
int number = message.Serializer.GetInt();
bool enabled = message.Serializer.GetBool();
```

Supported helpers include numeric primitives, strings, chars, booleans, byte arrays, numeric arrays, `INetSquareSerializable` objects, lists, and dictionaries.

## Request and Reply

Use the callback overload of `SendMessage` when the server should answer a specific request.

```csharp
client.SendMessage(new NetworkMessage(GameMessage.Ping).Set("ping"), reply =>
{
    string response = reply.Serializer.GetString();
    Console.WriteLine("Server replied: " + response);
});
```

The server must call `server.Reply(originalMessage, replyMessage)` for this callback to run.

## TCP and UDP

TCP is reliable and ordered:

```csharp
client.SendMessage(new NetworkMessage(GameMessage.Chat).Set("reliable payload"));
```

UDP is faster but unreliable:

```csharp
client.SendMessageUDP(new NetworkMessage(GameMessage.Chat).Set("unreliable payload"));
```

Connect with `NetSquareProtocoleType.TCP_AND_UDP` to enable both transports:

```csharp
client.Connect("127.0.0.1", 5555, NetSquareProtocoleType.TCP_AND_UDP);
```

If world synchronization should use UDP, keep `synchronizeUsingUDP` enabled:

```csharp
client.Connect("127.0.0.1", 5555, NetSquareProtocoleType.TCP_AND_UDP, synchronizeUsingUDP: true);
```

## Dispatcher

Register callbacks manually:

```csharp
client.Dispatcher.AddHeadAction(GameMessage.Chat, "Chat", message =>
{
    string text = message.Serializer.GetString();
});
```

Or auto-bind public static methods with `NetSquareActionAttribute`:

```csharp
using NetSquare.Core;

public static class ClientHandlers
{
    [NetSquareAction(GameMessage.Chat)]
    public static void OnChat(NetworkMessage message)
    {
        string text = message.Serializer.GetString();
    }
}
```

Enable auto-binding by constructing the client with `autoBindNetsquareActions: true`.

## Main Thread Dispatching

Callbacks can run from NetSquare worker threads. UI frameworks and Unity usually require work to run on the main thread. Use `SetMainThreadCallback` to marshal dispatch callbacks.

```csharp
using System;
using System.Collections.Concurrent;

ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

client.Dispatcher.SetMainThreadCallback((action, message) =>
{
    mainThreadQueue.Enqueue(() => action(message));
});

// Run this from your UI loop or Unity Update method.
while (mainThreadQueue.TryDequeue(out Action callback))
{
    callback();
}
```

## Server Time Synchronization

`SyncTime` estimates server time from a monotonic client clock and round-trip delay. Use an unscaled `Stopwatch` time source so local clock changes do not affect synchronization.

```csharp
using System.Diagnostics;

Stopwatch stopwatch = Stopwatch.StartNew();

client.SyncTime(
    getClientTime: () => (float)stopwatch.Elapsed.TotalSeconds,
    precision: 5,
    timeBetweenSyncs: 1000,
    onServerTimeGet: serverTime => Console.WriteLine("Server time: " + serverTime),
    onLog: Console.WriteLine);

float synchronizedTime = client.GetServerTime((float)stopwatch.Elapsed.TotalSeconds);
```

`SmoothServerTimeOffset` is enabled by default so offset changes are applied gradually. `TimeSynchronizationRequestTimeoutMs` bounds each request, and `TimeSynchronizationMaxAttempts` can cap retries when packets or replies are lost.

For long sessions, keep time synchronized automatically with a low-rate background refresh:

```csharp
client.StartAutoSyncTime(
    getClientTime: () => (float)stopwatch.Elapsed.TotalSeconds,
    precision: 3,
    timeBetweenSyncs: 50,
    intervalMs: 30000);

bool fresh = client.IsServerTimeSynchronizationFresh(45000);

client.StopAutoSyncTime();
```

## World Synchronization

Join a server world and send transform frames:

```csharp
client.WorldsManager.TryJoinWorld(1, new NetsquareTransformFrame(0, 0, 0), joined =>
{
    Console.WriteLine("Joined world: " + joined);
});

client.WorldsManager.OnClientJoinWorld += (clientID, transform, message) =>
{
    Console.WriteLine("Client joined world: " + clientID + " at " + transform);
};

client.WorldsManager.OnClientLeaveWorld += clientID =>
{
    Console.WriteLine("Client left world: " + clientID);
};

client.WorldsManager.OnReceiveSynchFrames += (clientID, frames) =>
{
    foreach (INetSquareSynchFrame frame in frames)
        Console.WriteLine("Frame from " + clientID + ": " + frame.SynchFrameType);
};

client.WorldsManager.SendSynchFrame(
    new NetsquareTransformFrame(_x: 10, _y: 0, _z: 5, _time: 1.25f));
```

You can queue frames and send them as a batch:

```csharp
client.WorldsManager.StoreSynchFrame(new NetsquareTransformFrame(_x: 1, _y: 0, _z: 0));
client.WorldsManager.StoreSynchFrame(new NetsquareTransformFrame(_x: 2, _y: 0, _z: 0));
client.WorldsManager.SendFrames();
```

## Useful Client Properties

- `ClientID`: current server-assigned ID.
- `IsConnected`: whether the TCP socket is connected.
- `NbSendingMessages`: pending outgoing messages.
- `NbProcessingMessages`: queued incoming messages waiting for dispatch.
- `ServerTimeOffset`: current estimated server time offset.
- `WorldsManager`: world membership and synchronization API.

## License

MIT
