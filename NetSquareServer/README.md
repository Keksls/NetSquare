# NetSquare.Server

`NetSquare.Server` is the server-side package for NetSquare. It provides a TCP server with optional UDP messaging, client ID management, message dispatching, request/reply support, broadcast helpers, runtime configuration, and basic world synchronization.

The package targets .NET Standard 2.0, .NET 8 for Windows, and .NET Framework 4.8. It includes `NetSquare_Server.dll` and depends on `NetSquare.Core`.

## Installation

```powershell
NuGet\Install-Package NetSquare.Server -Version 1.0.14
```

or:

```bash
dotnet add package NetSquare.Server --version 1.0.14
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
        NetSquareConfigurationManager.Initialize<NetSquareConfiguration>();
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

Configuration initialization is explicit and must happen before creating `NetSquareServer`. The consuming project can derive its own configuration contract; inherited NetSquare settings and project-owned settings are stored together in the same JSON file.

```csharp
public sealed class GameServerConfiguration : NetSquareConfiguration
{
    public int MaxPlayers { get; set; }
    public string DatabaseUrl { get; set; }

    public GameServerConfiguration()
    {
        MaxPlayers = 100;
        DatabaseUrl = "localhost";
    }
}

NetSquareConfigurationManager.Initialize<GameServerConfiguration>();
GameServerConfiguration config = NetSquareConfigurationManager.Get<GameServerConfiguration>();

config.Port = 5555;
config.NbQueueThreads = 2;
config.NbSendingThreads = 1;
config.ReceivingBufferSize = 4096;
config.UpdateFrequencyHz = 30;
config.LockConsole = false;
config.BlackListFilePath = @"[current]\BlackListedIP.json";
config.MaxPlayers = 250;

NetSquareConfigurationManager.Save();
```

By default, initialization reads or creates `config.json` in the current working directory. Pass a path to `Initialize<TConfiguration>(path)` to select another file. Reinitialization with a different type or path is rejected so a running server cannot silently switch configuration contract.

Important settings:

- `Port`: default server port.
- `NbQueueThreads`: number of message dispatch worker threads.
- `NbSendingThreads`: number of TCP sending threads.
- `ReceivingBufferSize`: receive buffer size.
- `UpdateFrequencyHz`: server update loop frequency.
- `LockConsole`: disables console quick-edit selection on Windows when enabled.
- `BlackListFilePath`: path for the blacklist file. `[current]` is replaced by the process working directory.
## Generic blacklist, escalation, and IP reputation

The blacklist is thread-safe and targets generic subjects. NetSquare provides the special **ip** subject type, while consuming projects can define **account**, **device**, or any other stable identity namespace.

Escalation level, active hit progress, hit-window expiration, and active bans are persisted. **BlackListPersistTemporaryBans** controls active temporary-ban persistence and **BlackListPersistHitProgress** controls current hit progress persistence. Permanent bans and escalation history are always persisted.

Configure one or more named policies before saving the NetSquare configuration:

    BlackListPolicy accountPolicy = new BlackListPolicy
    {
        Name = "account",
        HitWindowSeconds = 600,
        EscalationResetAfterSeconds = 0 // Zero keeps history until ClearHistory is called.
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
        BanType = BlackListBanType.Temporary,
        BanDurationSeconds = 30 * 60
    });
    accountPolicy.Stages.Add(new BlackListEscalationStage
    {
        HitThreshold = 5,
        BanType = BlackListBanType.Temporary,
        BanDurationSeconds = 60 * 60
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

When a policy has the same name as the subject type, it is selected automatically. Otherwise the configured default policy is used, or a caller can pass **policyName** explicitly. Existing configurations without **BlackListPolicies** are migrated to a single stage built from the previous scalar settings.

A consuming project owns account identity and reports incidents after authentication:

    BlackListSubject account = new BlackListSubject("account", accountId.ToString());

    BlackListHitResult result = BlackListManager.AddHit(
        account,
        hitCount: 1,
        reason: "Chat flood");

    if (result.IsBanned)
        server.DisconnectClient(clientId, result.CreateDisconnectInfo());

Administrative actions use the same generic subject:

    BlackListStatus status = BlackListManager.GetStatus(account);

    BlackListManager.Ban(
        account,
        BlackListBanType.Temporary,
        TimeSpan.FromMinutes(30),
        "Manual moderation");

    BlackListManager.Unban(account);         // Keeps the escalation level.
    BlackListManager.ClearHits(account);     // Clears only the active hit window.
    BlackListManager.ClearHistory(account);  // Resets history but keeps an active ban.

IP protection remains automatic and uses the generic engine through compatibility adapters:

    BlackListSubject ip = BlackListSubject.ForIPAddress("203.0.113.10");
    BlackListManager.AddHit(ip, reason: "Invalid transport data");

    BlackListManager.AddHit(client, reason: "Invalid handshake");
    BlackListManager.BanIP("203.0.113.10", BlackListBanType.Permanent);

Calling **AddHit(ConnectedClient)** sends **BannedTemporary** or **BannedPermanent**, including the optional expiration date, before closing the current TCP socket. For account subjects, the consuming project decides which connected client must be disconnected.

External reputation is only evaluated for **ip** subjects. Every provider is disabled by default:

    config.AbuseIPDBEnabled = true;
    config.AbuseIPDBApiKey = "YOUR_API_KEY";
    config.AbuseIPDBConfidenceThreshold = 75;
    config.AbuseIPDBMaximumDailyChecks = 1000;

    config.BlockListDeEnabled = true;
    config.BlockListDeMinimumAttacks = 10;
    config.BlockListDeMinimumReports = 1;

    config.SpamhausDropEnabled = true;
    config.DShieldEnabled = true;

The API key is redacted from server logs. External reputation never blocks the accepting thread: uncached public IPs are allowed while evaluation runs in the background, and provider failures fail open. Custom providers can be registered with **BlackListManager.RegisterReputationProvider(new MyIPReputationProvider())**.

BlockList.de uses https://api.blocklist.de/api.php?ip=...&start=1. AbuseIPDB uses the authenticated API v2 and respects **AbuseIPDBMaximumDailyChecks**. Spamhaus DROP and DShield usage remains subject to their respective attribution and service terms.
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

## Typed Disconnections

The server can send a generic reason before closing an established client socket:

```csharp
server.DisconnectClient(clientID, DisconnectReason.Kicked);

server.DisconnectClient(
    clientID,
    new DisconnectInfo(
        DisconnectReason.ServerRequest,
        "Maintenance window"));
```

`Stop()` automatically sends `ServerShutdown`. Heartbeat expiration sends `Timeout`, and blacklist actions send `BannedTemporary` or `BannedPermanent`.
## Shutdown

Call `Stop()` for a clean shutdown. The server sends disconnect notices, stops listeners, stops message queues, disconnects clients, and clears listener state.

```csharp
server.Stop();
```

## License

MIT
