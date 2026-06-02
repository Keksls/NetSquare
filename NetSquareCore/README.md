# NetSquare.Core

`NetSquare.Core` contains the shared primitives used by `NetSquare.Client` and `NetSquare.Server`: network messages, serialization helpers, dispatcher routing, compression and encryption abstractions, protocol helpers, UDP client support, and synchronization frames.

The package targets .NET Standard 2.0, .NET 8, and .NET Framework 4.8. It is installed automatically when you install current `NetSquare.Client` or `NetSquare.Server` packages.

## Installation

```powershell
NuGet\Install-Package NetSquare.Core -Version 1.0.7
```

or:

```bash
dotnet add package NetSquare.Core --version 1.0.7
```

## Network Messages

Use `NetworkMessage` to write values in the order the receiver will read them.

```csharp
using NetSquare.Core;

public enum GameMessage : ushort
{
    Chat = 1,
    Ping = 2
}

NetworkMessage message = new NetworkMessage(GameMessage.Chat)
    .Set("hello")
    .Set(123)
    .Set(true);

string text = message.Serializer.GetString();
int number = message.Serializer.GetInt();
bool enabled = message.Serializer.GetBool();
```

Supported helpers include numeric primitives, strings, chars, booleans, byte arrays, numeric arrays, `INetSquareSerializable` objects, lists, and dictionaries.

## Dispatcher

Register handlers manually:

```csharp
NetSquareDispatcher dispatcher = new NetSquareDispatcher();

dispatcher.AddHeadAction(GameMessage.Chat, "Chat", message =>
{
    string text = message.Serializer.GetString();
});
```

Or auto-bind public static methods with `NetSquareActionAttribute`:

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

## Serialization

Implement `INetSquareSerializable` when a type should control its own binary representation.

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

## Compression and Encryption

`NetSquare.Core` includes reusable compression and encryption implementations such as `NoCompression`, `GZipCompressor`, `DeflateCompressor`, `NoEncryption`, `AES_Encryptor`, and `XOR_Encryptor`.

## Synchronization Frames

The synchronization frame types are shared by the client and server world managers.

```csharp
INetSquareSynchFrame frame = new NetsquareTransformFrame(
    _x: 10,
    _y: 0,
    _z: 5,
    _time: 1.25f);
```

## License

MIT
