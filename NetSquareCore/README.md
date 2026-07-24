# NetSquare.Core

`NetSquare.Core` contains the protocol and data types shared by `NetSquare.Client` and `NetSquare.Server`: network messages, binary serialization, dispatcher routing, configuration persistence, connection feedback, conditional compression, scheduling, UDP transport support, and synchronization frames.

The package targets .NET Standard 2.0, .NET 8, and .NET Framework 4.8. It is installed automatically with current Client and Server packages.

## Installation

```powershell
Install-Package NetSquare.Core -Version 1.0.16
```

```bash
dotnet add package NetSquare.Core --version 1.0.16
```

Install Core directly when building shared contracts or tools. Applications normally install `NetSquare.Client` or `NetSquare.Server`, which reference the exact matching Core version.

## Version compatibility

Client, Server, and Core must use the same package version. The current handshake compares the Core assembly version exactly and rejects mismatched peers before the connected event is raised.

## Network messages

Every message has a route identifier (`HeadID`) and an ordered payload. Write and read values in exactly the same order and with matching types:

```csharp
using NetSquare.Core;

public enum GameMessage : ushort
{
    Chat = 1
}

NetworkMessage outgoing = new NetworkMessage(GameMessage.Chat)
    .Set("hello")
    .Set(123)
    .Set(true);

string text = outgoing.Serializer.GetString();
int score = outgoing.Serializer.GetInt();
bool ready = outgoing.Serializer.GetBool();
```

The serializer supports numeric primitives, strings, characters, booleans, byte and numeric arrays, lists, dictionaries, and `INetSquareSerializable` objects.

Do not reuse a received message after its callback returns unless your application owns all referenced state. Create a new `NetworkMessage` when forwarding or changing a payload.

## Custom serialization

Implement `INetSquareSerializable` when a type should control its compact binary representation:

```csharp
public sealed class PlayerState : INetSquareSerializable
{
    public string Name;
    public int Score;

    public void Serialize(NetSquareSerializer serializer)
    {
        serializer.Set(Name);
        serializer.Set(Score);
    }

    public void Deserialize(NetSquareSerializer serializer)
    {
        Name = serializer.GetString();
        Score = serializer.GetInt();
    }
}
```

Both peers must use the same schema. Add fields compatibly or coordinate a version change when the wire representation changes.

## Dispatcher

Register a handler manually:

```csharp
NetSquareDispatcher dispatcher = new NetSquareDispatcher();

dispatcher.AddHeadAction(GameMessage.Chat, "Chat", message =>
{
    string text = message.Serializer.GetString();
});
```

Or auto-bind public static methods:

```csharp
public static class Handlers
{
    [NetSquareAction(GameMessage.Chat)]
    public static void OnChat(NetworkMessage message)
    {
        string text = message.Serializer.GetString();
    }
}
```

Dispatcher callbacks may run on worker threads. Marshal UI and game-engine work to the required main thread.

## Shared JSON configuration

`NetSquareConfigurationStore<TConfiguration>` is the common strongly typed JSON persistence layer. Client and Server wrap it with package-specific managers and separate configuration files.

```csharp
using NetSquare.Core.Configuration;

NetSquareConfigurationStore<MyConfiguration> store =
    new NetSquareConfigurationStore<MyConfiguration>("application.config.json");

MyConfiguration configuration = store.Configuration;
configuration.UseTLS = true;
store.Save();
```

Configuration types derive from `NetSquareConfiguration`. Client-only settings live in `NetSquareClientConfiguration`; certificate, blacklist, listener, and server-worker settings live in the Server configuration contract.

## Scheduler

`NetSquareScheduler` runs named periodic callbacks through a shared coordinator and a bounded worker pool:

```csharp
NetSquareScheduler.AddAction(
    name: "game-heartbeat",
    frequency: 1000,
    enableSmartFrequencyAdjusting: true,
    callback: () => Console.WriteLine("tick"));

NetSquareScheduler.StartAction("game-heartbeat");

// Change the interval without restarting the action.
NetSquareScheduler.SetSchedulerFrequency("game-heartbeat", 500);

NetSquareScheduler.StopAction("game-heartbeat");
NetSquareScheduler.RemoveAction("game-heartbeat");
```

Frequencies passed as `int` are milliseconds. Frequencies passed as `float` are hertz. Keep callbacks bounded: a runner never overlaps itself, and an unhandled callback exception stops that schedule.

## Connection feedback contracts

`ConnectionRejectionInfo` and `DisconnectInfo` provide:

- a typed reason;
- an optional human-readable message;
- an optional UTC expiration date.

The reason enums distinguish temporary and permanent bans. `IsBanned`, `IsTemporaryBan`, and `IsPermanentBan` simplify client handling.

## Message compression

Compression is disabled by default. Enable the shared Deflate policy before creating or sending messages:

```csharp
using NetSquare.Core.Compression;

NetworkMessageCompression.Enabled = true;
NetworkMessageCompression.MinimumBodyLength = 256;
NetworkMessageCompression.MinimumSavings = 16;
```

Each message carries an explicit compression flag. NetSquare uses the fast Deflate level only when the body reaches the configured threshold and the final representation saves at least `MinimumSavings` bytes. Small and incompressible messages retain the pooled uncompressed path.

Decompression always validates the declared output size against `NetworkMessage.MaxDecodedMessageSize`, regardless of whether local sending compression is enabled.

Do not place secrets and attacker-controlled values in the same compressed message when its encoded length is observable. TLS protects message contents but does not remove compression length side channels.

Application-level encryption was removed. Use TLS for authenticated TCP confidentiality. Authenticated UDP intentionally provides integrity and replay protection without confidentiality.

## Synchronization frames

Frame contracts are shared by Client and Server world managers:

```csharp
INetSquareSynchFrame frame = new NetsquareTransformFrame(
    _x: 10,
    _y: 0,
    _z: 5,
    _time: 1.25f);
```

The Server can bound retained frames per client and spatialize delivery; the Client can batch frames and choose TCP or UDP synchronization.

## Security

TLS and authenticated UDP are configured by the Client and Server packages. See the [handshake and transport security guide](https://github.com/Keksls/NetSquare/blob/main/HANDSHAKE.md).

## License

MIT
