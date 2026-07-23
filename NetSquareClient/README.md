# NetSquare.Client

`NetSquare.Client` is the client-side package for NetSquare. It provides a TCP client with optional UDP messaging, request/reply callbacks, dispatcher-based message routing, server time synchronization, and world synchronization helpers.

The package targets .NET Standard 2.0, .NET 8, and .NET Framework 4.8. It includes `NetSquareClient.dll` and depends on `NetSquare.Core`.

## Installation

```powershell
NuGet\Install-Package NetSquare.Client -Version 1.0.14
```

or:

```bash
dotnet add package NetSquare.Client --version 1.0.14
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

## JSON Configuration

Client and server configuration files use the same loader from `NetSquare.Core`, but keep separate strongly typed contracts and files. Initialize the client manager once, then pass its configuration to the client:

```csharp
NetSquareClientConfigurationManager.Initialize<NetSquareClientConfiguration>();
NetSquareClientConfiguration configuration =
    NetSquareClientConfigurationManager.Get<NetSquareClientConfiguration>();

NetSquareClient client = new NetSquareClient(configuration);
client.Connect();
```

The default client path is `client.config.json`. It is created with complete defaults when missing. A typical file is:

```json
{
  "Host": "game.example.com",
  "Port": 5555,
  "ProtocoleType": 1,
  "UseTLS": true,
  "TLSServerName": "game.example.com",
  "ConnectionTimeoutMilliseconds": 30000,
  "HeartbeatEnabled": true,
  "HeartbeatIntervalMilliseconds": 10000,
  "HeartbeatTimeoutMilliseconds": 30000,
  "SmoothServerTimeOffset": true,
  "ServerTimeOffsetSmoothingSpeed": 8,
  "TimeSynchronizationRequestTimeoutMilliseconds": 1500,
  "TimeSynchronizationMaxAttempts": 0,
  "SynchronizationTransport": 1,
  "MaxStoredSynchronizationFrames": 256,
  "AutoSendSynchronizationFrames": true
}
```

`ProtocoleType` uses `0` for TCP and `1` for TCP plus UDP. `SynchronizationTransport` uses `0` for reliable TCP and `1` for unreliable UDP. UDP synchronization requires the TCP-plus-UDP protocol.

`TLSServerName` is optional. Leave it empty to validate the certificate against `Host`, or set it when connecting by IP to a certificate issued for a DNS name. Custom certificate callbacks remain code-only:

```csharp
client.TLSCertificateValidationCallback = ValidatePrivateCertificate;
```

Call `NetSquareClientConfigurationManager.Save()` after changing settings that should be persisted. Custom projects may derive from `NetSquareClientConfiguration` and initialize the manager with their derived type.

## Async Connection

`ConnectAsync` returns one typed result instead of splitting control flow between exceptions and events:

```csharp
using System.Threading;

using CancellationTokenSource cancellation = new CancellationTokenSource();

ConnectionResult result = await client.ConnectAsync(
    "127.0.0.1",
    5555,
    timeoutMilliseconds: 10000,
    cancellationToken: cancellation.Token);

switch (result.Status)
{
    case ConnectionResultStatus.Connected:
        Console.WriteLine("Connected as " + result.ClientID);
        break;

    case ConnectionResultStatus.Rejected:
        Console.WriteLine("Rejected: " + result.RejectionInfo.Reason);
        break;

    case ConnectionResultStatus.TimedOut:
    case ConnectionResultStatus.TransportError:
        Console.WriteLine(result.Exception);
        break;

    case ConnectionResultStatus.Cancelled:
        Console.WriteLine("Connection cancelled");
        break;
}
```

Set `ConnectionTimeoutMilliseconds` to change the default 30-second timeout, pass a `CancellationToken`, call `CancelConnectionAttempt()`, or call `Disconnect()` while the attempt is pending. Only one connection attempt can run at a time.

The existing `Connect()` methods remain available as non-blocking compatibility wrappers. They use `ConnectAsync` internally and publish the existing connection events from its typed result.
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

## Typed Connection Feedback

Connection refusal and disconnection feedback is delivered before the server closes the socket:

```csharp
client.OnConnectionRejected += info =>
{
    Console.WriteLine("Connection rejected: " + info.Reason);
    if (info.ExpiresUtc.HasValue)
        Console.WriteLine("Ban expires at: " + info.ExpiresUtc.Value);
};

client.OnDisconnected += info =>
{
    Console.WriteLine("Disconnected: " + info.Reason);
};
```

`ConnectionRejectionReason` and `DisconnectReason` distinguish temporary and permanent bans through `BannedTemporary` and `BannedPermanent`. The existing `OnConnectionFail` event remains reserved for transport failures that do not include server feedback.
## Useful Client Properties

- `ClientID`: current server-assigned ID.
- `ConnectionTimeoutMilliseconds`: default timeout used by `Connect` and `ConnectAsync`.
- `IsConnected`: whether the TCP socket is connected.
- `NbSendingMessages`: pending outgoing messages.
- `NbProcessingMessages`: queued incoming messages waiting for dispatch.
- `ServerTimeOffset`: current estimated server time offset.
- `WorldsManager`: world membership and synchronization API.

## License

MIT
