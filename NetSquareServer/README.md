# NetSquare.Server

`NetSquare.Server` provides TCP listening, optional authenticated UDP, message dispatch, request/reply handling, client management, JSON configuration, generic blacklist policies, world synchronization, and spatialization.

The package targets .NET Standard 2.0, .NET 8 for Windows, and .NET Framework 4.8. It installs the exact matching `NetSquare.Core` version automatically.

## Installation

```powershell
Install-Package NetSquare.Server -Version 1.0.17
```

```bash
dotnet add package NetSquare.Server --version 1.0.17
```

The Server and Client must use the same package version.

## Quick start

```csharp
using NetSquare.Core;
using NetSquare.Server;

public enum GameMessage : ushort
{
    Chat = 1,
    Ping = 2,
    Welcome = 3
}

NetSquareConfigurationManager.Initialize<NetSquareConfiguration>();

NetSquareServer server =
    new NetSquareServer(NetSquareProtocoleType.TCP_AND_UDP);

server.OnClientConnected += clientID =>
{
    Console.WriteLine("Connected: " + clientID);
    server.SendToClient(
        new NetworkMessage(GameMessage.Welcome)
            .Set("Welcome")
            .Set(clientID),
        clientID);
};

server.OnClientDisconnected += clientID =>
    Console.WriteLine("Disconnected: " + clientID);

server.Dispatcher.AddHeadAction(GameMessage.Chat, "Chat", message =>
{
    string text = message.Serializer.GetString();
    server.Broadcast(
        new NetworkMessage(GameMessage.Chat)
            .Set(message.ClientID)
            .Set(text));
});

server.Dispatcher.AddHeadAction(GameMessage.Ping, "Ping", message =>
{
    server.Reply(message, new NetworkMessage().Set("pong"));
});

server.Start(
    port: 5555,
    allowLocalIP: true,
    bindDispatcher: false,
    CheckBlackList: true);
```

Call `Stop()` during application shutdown.

## JSON configuration

Configuration initialization must happen before constructing `NetSquareServer`:

```csharp
public sealed class GameServerConfiguration : NetSquareConfiguration
{
    public int MaxPlayers { get; set; } = 100;
    public string DatabaseUrl { get; set; } = "localhost";
}

NetSquareConfigurationManager.Initialize<GameServerConfiguration>();

GameServerConfiguration config =
    NetSquareConfigurationManager.Get<GameServerConfiguration>();

config.Port = 5555;
config.NbQueueThreads = 2;
config.NbSendingThreads = 1;
config.ReceivingBufferSize = 4096;
config.UpdateFrequencyHz = 30;
config.BlackListFilePath = @"[current]\BlackListedIP.json";
config.MaxPlayers = 250;

NetSquareConfigurationManager.Save();
```

The default file is `config.json`. Pass a path to `Initialize<TConfiguration>(path)` to use another file. `[current]` is replaced by the process working directory in supported path settings.

Important settings include:

- `Port`: default listener port.
- `NbQueueThreads`: dispatcher worker count.
- `HeartbeatEnabled`, `HeartbeatIntervalMilliseconds`, and `HeartbeatTimeoutMilliseconds`: heartbeat policy imposed on every Client.
- `NbSendingThreads`: TCP send worker count.
- `ReceivingBufferSize`: TCP receive buffer size.
- `UpdateFrequencyHz`: Server update frequency.
- `BlackListFilePath`: persisted blacklist state.

## Heartbeat

The Server exclusively owns the heartbeat policy:

```csharp
config.HeartbeatEnabled = true;
config.HeartbeatIntervalMilliseconds = 10000;
config.HeartbeatTimeoutMilliseconds = 30000;
NetSquareConfigurationManager.Save();
```

When enabled, the interval must be at least 1000 milliseconds and the timeout must be greater than the interval. The final validated handshake frame sends this policy to the Client. The Client applies it before starting its heartbeat loop, while the Server uses the same timeout to disconnect silent TCP connections.

When disabled, the Client does not start a heartbeat loop and the Server does not apply heartbeat timeout checks.

## TLS and authenticated UDP

Enable TLS and provide a PFX or PKCS#12 certificate containing its private key:

```csharp
config.UseTLS = true;
config.TLSCertificatePath = @"[current]\certificates\netsquare.pfx";
config.TLSCertificatePassword = "use-a-secret-source";
NetSquareConfigurationManager.Save();
```

The Client must also enable TLS and validate the certificate name. Server construction fails immediately if TLS is enabled and the certificate is missing or unusable.

With TCP plus UDP, per-session keys authenticate UDP datagrams. TLS protects the session key while it is negotiated over TCP. See the [handshake and transport security guide](https://github.com/Keksls/NetSquare/blob/main/HANDSHAKE.md).

Handshake admission controls are static listener settings rather than JSON configuration properties. Set them before calling `Start()`:

```csharp
NetSquare.Server.TcpListener.ListenBacklog = 1024;
NetSquare.Server.TcpListener.ClientHelloTimeoutMilliseconds = 2000;
NetSquare.Server.TcpListener.HandshakeTimeoutMilliseconds = 5000;
NetSquare.Server.TcpListener.MaxConcurrentHandshakes = 256;
NetSquare.Server.TcpListener.MaxConcurrentHandshakesPerAddress = 4;
NetSquare.Server.TcpListener.ProofOfWorkActivationThreshold = 32;
```

## Messages, dispatcher, and replies

Write values with `Set(...)`; the receiver must read the same types in the same order:

```csharp
server.SendToClient(
    new NetworkMessage(GameMessage.Chat)
        .Set("hello")
        .Set(123),
    clientID);
```

Register handlers manually:

```csharp
server.Dispatcher.AddHeadAction(GameMessage.Chat, "Chat", message =>
{
    string text = message.Serializer.GetString();
});
```

Or auto-bind public static methods with `NetSquareActionAttribute` and start with `bindDispatcher: true`.

Reply to a Client callback request:

```csharp
server.Dispatcher.AddHeadAction(GameMessage.Ping, "Ping", message =>
{
    string request = message.Serializer.GetString();
    server.Reply(
        message,
        new NetworkMessage().Set("pong for " + request));
});
```

## Sending

```csharp
// One Client, reliable and ordered.
server.SendToClient(message, clientID);

// Selected Clients.
server.SendToClients(message, new uint[] { 1, 2, 3 });

// Every connected Client.
server.Broadcast(message);

// One Client, unreliable and replaceable.
server.SendToClientUDP(message, clientID);
```

Use TCP for commands and state that must arrive. Use UDP for replaceable, time-sensitive updates. When UDP sends outpace a socket, NetSquare keeps the newest pending payload per route instead of growing the queue without bound.

## Client management and typed disconnects

```csharp
bool connected = server.IsClientConnected(clientID);
ConnectedClient client = server.SafeGetClient(clientID);
int handshaking = server.GetNbVerifyingClients();

server.ReplaceClientID(oldID, newID);
server.DisconnectClient(clientID, DisconnectReason.Kicked);

server.DisconnectClient(
    clientID,
    new DisconnectInfo(
        DisconnectReason.ServerRequest,
        "Maintenance window"));
```

`Stop()` sends `ServerShutdown`. Heartbeat expiration sends `Timeout`. Temporary and permanent blacklist actions include the matching typed reason and optional expiration.

Applications may override ID allocation:

```csharp
uint nextID = 1000;
server.GetNewClientID = () => nextID++;
```

## Generic blacklist and escalation

The blacklist is thread-safe and works with generic subjects. NetSquare provides the special `ip` subject type; applications may define `account`, `device`, or another stable identity namespace.

Configure an escalation policy:

```csharp
BlackListPolicy accountPolicy = new BlackListPolicy
{
    Name = "account",
    HitWindowSeconds = 600,
    EscalationResetAfterSeconds = 0
};

accountPolicy.Stages.Add(new BlackListEscalationStage
{
    HitThreshold = 15,
    BanType = BlackListBanType.Temporary,
    BanDurationSeconds = 15 * 60
});

accountPolicy.Stages.Add(new BlackListEscalationStage
{
    HitThreshold = 5,
    BanType = BlackListBanType.Permanent
});

config.BlackListPolicies = new List<BlackListPolicy> { accountPolicy };
config.BlackListDefaultPolicyName = "account";
config.BlackListPersistTemporaryBans = true;
config.BlackListPersistHitProgress = true;
config.BlackListMaxTrackedSubjects = 10000;
NetSquareConfigurationManager.Save();
```

Report incidents after authenticating an application-owned account:

```csharp
BlackListSubject account =
    new BlackListSubject("account", accountID.ToString());

BlackListHitResult result = BlackListManager.AddHit(
    account,
    hitCount: 1,
    reason: "Chat flood");

if (result.IsBanned)
    server.DisconnectClient(clientID, result.CreateDisconnectInfo());
```

Administrative operations are explicit:

```csharp
BlackListStatus status = BlackListManager.GetStatus(account);

BlackListManager.Ban(
    account,
    BlackListBanType.Temporary,
    TimeSpan.FromMinutes(30),
    "Manual moderation");

BlackListManager.Unban(account);         // Keep escalation history.
BlackListManager.ClearHits(account);     // Clear the active hit window.
BlackListManager.ClearHistory(account);  // Reset history, keep an active ban.
```

IP helpers use the same engine:

```csharp
BlackListSubject ip =
    BlackListSubject.ForIPAddress("203.0.113.10");

BlackListManager.AddHit(ip, reason: "Invalid transport data");
BlackListManager.BanIP(
    "203.0.113.10",
    BlackListBanType.Permanent);
```

External reputation providers are optional and disabled by default. AbuseIPDB, BlockList.de, Spamhaus DROP, and DShield apply only to `ip` subjects. Uncached checks run in the background and provider failures fail open.

## Worlds

Create and inspect worlds:

```csharp
using NetSquare.Server.Worlds;
using NetSquare.Server.Worlds.Spatialization;

NetSquareServer server =
    new NetSquareServer(
        NetSquareProtocoleType.TCP_AND_UDP,
        useWorldManager: true);

NetSquareWorld arena = server.Worlds.AddWorld(1, "Arena", 32);

bool inWorld = server.Worlds.IsInWorld(clientID);
ushort worldID = server.Worlds.GetClientWorldID(clientID);
```

World events expose joins, leaves, and movement. Broadcast through `WorldsManager` when the sender already belongs to a world:

```csharp
server.Worlds.BroadcastToWorld(
    new NetworkMessage(GameMessage.Chat, clientID)
        .Set("world message"));
```

## Spatialization and synchronization

Use a simple distance spatializer:

```csharp
SimpleSpatializer spatializer = Spatializer.GetSimpleSpatializer(
    arena,
    spatializationFreq: 10,
    synchFreq: 20,
    maxViewDistance: 100,
    visibilityHysteresis: 5);

spatializer.MaxStoredFramesPerClient = 256;
spatializer.SetAdaptiveSynchFrequency(
    min: 10,
    max: 30,
    maxKeepingLastFrequencies: 30,
    synchMinimumOffset: 2);

arena.SetSpatializer(spatializer);
spatializer.Start();
```

`visibilityHysteresis` keeps an already-visible Client visible slightly beyond the entry distance, reducing churn near a boundary.

For grid-based worlds:

```csharp
ChunkedSpatializer spatializer = Spatializer.GetChunkedSpatializer(
    arena,
    spatializationFreq: 10,
    synchFreq: 20,
    chunkSize: 50,
    xStart: -500,
    yStart: -500,
    xEnd: 500,
    yEnd: 500,
    chunkHysteresis: 2,
    maximumChunkCount: 65536);
```

`chunkHysteresis` prevents rapid chunk changes near cell edges. `maximumChunkCount` rejects an
oversized grid before allocation; the overload without this argument uses
`ChunkedSpatializer.DefaultMaximumChunkCount` (`65536`). Bounds and chunk size must be finite.
`MaxStoredFramesPerClient` bounds retained synchronization state; older frames are discarded when
a producer outruns synchronization.

Stop a spatializer before replacing or discarding it.

## Threading and shutdown

Network callbacks, dispatcher handlers, scheduler actions, and synchronization work run on background threads. Keep callbacks short and synchronize access to application state.

Call `Stop()` for a clean shutdown:

```csharp
server.Stop();
```

It sends typed disconnect notices, stops listeners and worker queues, disconnects Clients, and clears listener state.

## License

MIT
