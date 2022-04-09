# NetSquare - a C# Tcp solution

## Server
First Import NetSquareCore.dll and NetSquareServer.dll to your project.
Instantiate a NetSquare_Server object by doing :
```
NetSquare_Server server = new NetSquare_Server();
```
Then start the server by giving it a port
```
server.Start(5555);
```

### Dispatcher
NetSquare provide a dispatching system that will invoke methods for you.
Just tag Methods with the NetSquareActionAttribute and give a unique ID.
On server start, the dispatcher will map every methods that have this attribute and delegate them for being invoked when a client will send message that correspond to the given ID.
All methods mapped by the attribut must be public and static, and must have a NetworkMessage parameter only.
```
[NetSquareAction(0)]
public static void ClientSendText(NetworkMessage message)
{
    MessageBox.Show(message.GetString());
}
```

You can manualy add NetSquareAction to the dispatcher by doing the following 
```
server.Dispatcher.AddHeadAction(1, "Ping", ClientPingMe);
```
Where ClientPingMe is a method that can be private and non static, but must still have a NetworkMessage parameter only.

### NetworkMessage
NetSquare use a custom data model for sharing messages between clients and server.
These are NetworkMessage. They handle serialization, Encryption and Compression for you (see 'Protocole' section for more about Compression and Encryption).
NetworkMessage can serialize anything you want. It Handle primitive types and complex objects. Complex objects are serialized using Bson format. It's quite fast, but alwas prefere primitive types if you can (sending some positions or rotations must be sended with x, y and z as 3 float instead of sending a vector3. The message will be smaller and faster to serialize/deserialize).

#### Sending a message
Server can send message to Specific client, using TcpClient instance or ClientID.
When a client is connected to the server, the event will give you the network ID of the client. It will be unique, and NetSquare will handle it for you.
It can send message to a ist of client or broadcast to anyone.
Eg: sending a text message to a specific client :
```
server.SendToClient(new NetworkMessage(0).Set("Welcome to my NetSquare server"), clientID);
```
***this will send a string ("Welcome to my NetSquare server") to the client 'clientID'***
You must give the message ID to the NertworkMessage Constructor. It's the ID that will help the dispatcher to invoke the right method. You can call 'Set' anytime you want. Set can take anything as parameter, primitive and complex types, and can be Stacked.
```
eg: .set([int]).set([string]).set([customType])
```

#### Reading a message

#### Configuration
You can use configuration system to specify persistants parameters
```
NetSquareConfigurationManager.Configuration.BlackListFilePath = @"[current]\blackList.bl";
NetSquareConfigurationManager.Configuration.LockConsole = false;
NetSquareConfigurationManager.Configuration.Port = 5050;
NetSquareConfigurationManager.Configuration.ProcessOffsetTime = 1;
NetSquareConfigurationManager.Configuration.NbReceivingThreads = 4;
NetSquareConfigurationManager.Configuration.NbQueueThreads = 8;
NetSquareConfigurationManager.SaveConfiguration(config);
```
###### BlackListFilePath
Path to the BlackList IP File. NetSquare use external API to determinate if a client is a knew abusive IP.
You can manualy add IP to the BlackListed IP file. It's a json file that NetSquare created a first run to the given path.

###### LockConsole
If LockConsole is set to True and your server run to a Windows Console, the console will be locked for preventing user selection that lock main thread

###### Port
The port you want to start server on

###### ProcessOffsetTime
Sleep time to wait in ms before checking received message queues. minimum 1. the more this number is low, the more fast will be the server

###### NbReceivingThreads
Number of thread that will handle client message reading. keep this number between 1 and the number of core of your chip. (1-4 is greate, default is 1. Change it for large number of clients).

###### NbQueueThreads
Number of thread that will process received messages. After client send message, thos will be stored into some queues, waiting to be processed by dispatcher. This number represent the number of parralles traitments dispatcher will performe.
Same as 'NbReceivingThreads', keep this number between 1 and the number of core of your chip. (1-4 is greate, default is 1. Change it for large number of clients).

#### Writer
NetSquare provide a Writing solution for display debug informations into console and save logs.
Instade of using Console.Write(), use Writer.Write().

```
Writer.StartRecordingLog();
Writer.StartDisplayTitle();
Writer.StopDisplayLog();
```
###### Start/StopRecordingLog()
The Writer will start recording log into a specific thread for keeping smooth performances.

###### Start/StopDisplayTitle()
The Writer will display debug informations into console title. A bit performance costly but not to mutch.

###### Start/StopDisplayLog()
The Writer will display debug informations into console. Windows console Write is incredibly slow, use it only for debug.