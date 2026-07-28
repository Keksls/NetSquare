# NetSquare.Client

`NetSquare.Client` provides TCP connections, optional authenticated UDP, request/reply callbacks, dispatcher routing, typed connection results, server time synchronization, and world synchronization.

The package targets .NET Standard 2.0, .NET 8, and .NET Framework 4.8. It installs the exact matching `NetSquare.Core` version automatically.

## Installation

```powershell
Install-Package NetSquare.Client -Version 1.0.18
```

```bash
dotnet add package NetSquare.Client --version 1.0.18
```

The Server and Client must use the same package version.

## Quick start

```csharp
using NetSquare.Client;
using NetSquare.Core;

public enum GameMessage : ushort
{
    Chat = 1,
    Welcome = 2,
    Ping = 3,
    Transform = 4
}

NetSquareClient client = new NetSquareClient(autoBindNetsquareActions: false);

client.OnConnected += clientID =>
{
    Console.WriteLine("Connected as " + clientID);
    client.SendMessage(
        new NetworkMessage(GameMessage.Chat).Set("Hello server"));
};

client.OnDisconnected += info =>
    Console.WriteLine("Disconnected: " + info.Reason);

client.OnConnectionFail += () =>
    Console.WriteLine("The transport connection failed");

client.OnException += exception =>
    Console.WriteLine(exception);

client.Dispatcher.AddHeadAction(GameMessage.Welcome, "Welcome", message =>
{
    string text = message.Serializer.GetString();
    uint assignedClientID = message.Serializer.GetUInt();
    Console.WriteLine(text + " - client " + assignedClientID);
});

client.Connect("127.0.0.1", 5555, NetSquareProtocoleType.TCP_AND_UDP);
```

Call `Disconnect()` during application shutdown.

## Async connection

`ConnectAsync` reports success, rejection, timeout, cancellation, and transport errors through one typed result:

```csharp
using System.Threading;

using CancellationTokenSource cancellation = new CancellationTokenSource();

ConnectionResult result = await client.ConnectAsync(
    "game.example.com",
    5555,
    NetSquareProtocoleType.TCP_AND_UDP,
    synchronizeUsingUDP: true,
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

Only one connection attempt can run at a time. Cancel it with the supplied token, `CancelConnectionAttempt()`, or `Disconnect()`.

The non-blocking `Connect()` overloads remain available and publish the existing connection events.

## JSON configuration

Initialize the configuration manager once, then pass the configuration to the client:

```csharp
NetSquareClientConfigurationManager.Initialize<NetSquareClientConfiguration>();

NetSquareClientConfiguration configuration =
    NetSquareClientConfigurationManager.Get<NetSquareClientConfiguration>();

NetSquareClient client = new NetSquareClient(configuration);
client.Connect();
```

The default file is `client.config.json`. It is created with complete defaults when missing:

```json
{
  "Host": "game.example.com",
  "Port": 5555,
  "ProtocoleType": 1,
  "UseTLS": true,
  "TLSServerName": "game.example.com",
  "ConnectionTimeoutMilliseconds": 30000,
  "MaxPendingReplyCallbacks": 4096,
  "ReplyCallbackTimeoutMilliseconds": 30000,
  "SmoothServerTimeOffset": true,
  "ServerTimeOffsetSmoothingSpeed": 8,
  "TimeSynchronizationRequestTimeoutMilliseconds": 1500,
  "TimeSynchronizationMaxAttempts": 0,
  "SynchronizationTransport": 1,
  "MaxStoredSynchronizationFrames": 256,
  "AutoSendSynchronizationFrames": true
}
```

`ProtocoleType` uses `0` for TCP and `1` for TCP plus UDP. `SynchronizationTransport` uses `0` for TCP and `1` for UDP. UDP synchronization requires TCP plus UDP.

Call `NetSquareClientConfigurationManager.Save()` after changing settings that should persist. Applications may derive from `NetSquareClientConfiguration` to store their own settings in the same file.

Heartbeat settings are not part of the Client configuration. The Server sends its enabled state, interval, and timeout in the final validated handshake frame; the Client applies that policy before starting its heartbeat loop.

## TLS

Set `UseTLS` on both peers. `TLSServerName` is optional: leave it empty to validate against `Host`, or set it when connecting by IP to a certificate issued for a DNS name.

Private certificate authorities can use a code-only validation callback:

```csharp
client.TLSCertificateValidationCallback = ValidatePrivateCertificate;
```

Production code must not accept every certificate. TLS authenticates and encrypts TCP and protects the UDP session key negotiated during the handshake.

## Messages and replies

Write values with `Set(...)` and read them in the same order:

```csharp
client.SendMessage(
    new NetworkMessage(GameMessage.Chat)
        .Set("hello")
        .Set(123));
```

Use the callback overload when the Server will call `Reply`:

```csharp
client.SendMessage(new NetworkMessage(GameMessage.Ping).Set("ping"), reply =>
{
    string response = reply.Serializer.GetString();
    Console.WriteLine(response);
});
```
Pending reply callbacks are bounded by `MaxPendingReplyCallbacks` and expire after
`ReplyCallbackTimeoutMilliseconds` unless an internal operation supplies a shorter timeout. A
late reply never invokes an expired callback, and disconnecting clears every pending callback.

## TCP and UDP

Use TCP for data that must arrive reliably and in order:

```csharp
client.SendMessage(
    new NetworkMessage(GameMessage.Chat).Set("reliable"));
```

Use UDP only for replaceable, time-sensitive state:

```csharp
client.SendMessageUDP(
    new NetworkMessage(GameMessage.Transform)
        .Set(10f)
        .Set(0f)
        .Set(5f));
```

UDP may be lost or reordered. When sends outpace the socket, NetSquare keeps the newest pending payload per route instead of growing the queue without bound.

With current TCP-plus-UDP connections, datagrams carry a sequence and truncated HMAC derived from a per-session key. The TCP session remains the source of client identity.

## Dispatcher and main thread

Register callbacks manually or enable `NetSquareActionAttribute` binding through the constructor:

```csharp
client.Dispatcher.AddHeadAction(GameMessage.Chat, "Chat", message =>
{
    string text = message.Serializer.GetString();
});
```

Callbacks can run on NetSquare worker threads. UI frameworks and Unity normally require main-thread work:

```csharp
ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

client.Dispatcher.SetMainThreadCallback((action, message) =>
{
    mainThreadQueue.Enqueue(() => action(message));
});

// Run from the UI loop or Unity Update.
while (mainThreadQueue.TryDequeue(out Action callback))
    callback();
```

## Server time synchronization

Use an unscaled monotonic clock:

```csharp
Stopwatch stopwatch = Stopwatch.StartNew();

client.SyncTime(
    getClientTime: () => (float)stopwatch.Elapsed.TotalSeconds,
    precision: 5,
    timeBetweenSyncs: 1000,
    onServerTimeGet: serverTime => Console.WriteLine(serverTime));

float serverTime =
    client.GetServerTime((float)stopwatch.Elapsed.TotalSeconds);
```

For long sessions:

```csharp
client.StartAutoSyncTime(
    getClientTime: () => (float)stopwatch.Elapsed.TotalSeconds,
    precision: 3,
    timeBetweenSyncs: 50,
    intervalMs: 30000);

bool fresh = client.IsServerTimeSynchronizationFresh(45000);
client.StopAutoSyncTime();
```

## World synchronization

Join a world and publish transform frames:

```csharp
client.WorldsManager.TryJoinWorld(
    1,
    new NetsquareTransformFrame(0, 0, 0),
    joined => Console.WriteLine("Joined: " + joined));

client.WorldsManager.OnReceiveSynchFrames += (clientID, frames) =>
{
    foreach (INetSquareSynchFrame frame in frames)
        Console.WriteLine(clientID + ": " + frame.SynchFrameType);
};

client.WorldsManager.OnWorldRemoved += removedWorldID =>
    Console.WriteLine("World removed: " + removedWorldID);

client.WorldsManager.SendSynchFrame(
    new NetsquareTransformFrame(_x: 10, _y: 0, _z: 5, _time: 1.25f));
```

Batch multiple frames with `StoreSynchFrame(...)` followed by `SendFrames()`. Configure `MaxStoredSynchronizationFrames` to bound client-side pending state.

When the Server removes the active world, the Client clears its local membership and pending
synchronization frames before raising `OnWorldRemoved`.

## Typed connection feedback

```csharp
client.OnConnectionRejected += info =>
{
    Console.WriteLine("Rejected: " + info.Reason);
    if (info.ExpiresUtc.HasValue)
        Console.WriteLine("Expires: " + info.ExpiresUtc.Value);
};

client.OnDisconnected += info =>
    Console.WriteLine("Disconnected: " + info.Reason);
```

`OnConnectionFail` is reserved for transport failures without typed Server feedback.

## Useful properties

- `ClientID`: current Server-assigned ID.
- `IsConnected`: whether the TCP connection is active.
- `ConnectionTimeoutMilliseconds`: default connection timeout.
- `NbSendingMessages`: pending outgoing TCP messages.
- `NbProcessingMessages`: received messages waiting for dispatch.
- `ServerTimeOffset`: estimated Server time offset.
- `WorldsManager`: world membership and synchronization API.

## License

MIT
