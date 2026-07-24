# Configuration client et serveur

NetSquare utilise des fichiers JSON fortement typés. Le client et le serveur ont chacun leur propre gestionnaire et leur propre fichier par défaut :

| Côté | Gestionnaire | Type de base | Fichier par défaut |
|---|---|---|---|
| Client | `NetSquareClientConfigurationManager` | `NetSquareClientConfiguration` | `client.config.json` |
| Serveur | `NetSquareConfigurationManager` | `NetSquareConfiguration` | `config.json` |

Les chemins par défaut sont relatifs au répertoire de travail du processus, puis convertis en chemins absolus.

## Fonctionnement commun

L’initialisation doit être faite une seule fois, avant de construire le client ou le serveur. Utilisez le gestionnaire propre au côté concerné : `NetSquareClientConfigurationManager` pour le client ou `NetSquareConfigurationManager` pour le serveur.

Au premier appel à `Initialize<T>()` :

1. le fichier JSON existant est chargé ;
2. s’il n’existe pas, une instance avec les valeurs par défaut est créée et enregistrée ;
3. le type concret et le chemin deviennent ceux du gestionnaire pour toute la durée du processus.

Rappeler `Initialize<T>()` avec le même type et le même chemin est autorisé. Essayer ensuite d’utiliser un autre type ou un autre fichier lève une `InvalidOperationException`.

Il n’y a pas de rechargement automatique lorsque le JSON est modifié sur disque. Pour une configuration de déploiement, modifiez le fichier avant le démarrage ou redémarrez le processus. Pour une modification en code, changez l’objet retourné par `Get<T>()`, puis appelez `Save()`.

## Configuration client

### Initialisation et connexion

```csharp
using System.Threading;
using System.Threading.Tasks;
using NetSquare.Client;

NetSquareClientConfigurationManager
    .Initialize<NetSquareClientConfiguration>();

NetSquareClientConfiguration configuration =
    NetSquareClientConfigurationManager
        .Get<NetSquareClientConfiguration>();

NetSquareClient client = new NetSquareClient(configuration);

ConnectionResult result =
    await client.ConnectAsync(CancellationToken.None);
```

Un chemin personnalisé peut être fourni :

```csharp
NetSquareClientConfigurationManager
    .Initialize<NetSquareClientConfiguration>(
        @"Configuration\client.production.json");
```

Les principaux groupes de réglages client sont :

- connexion : `Host`, `Port`, `ProtocoleType` et `ConnectionTimeoutMilliseconds` ;
- sécurité : `UseTLS`, `TLSServerName` et `UseUdpAuthentication` ;
- heartbeat : `HeartbeatEnabled`, `HeartbeatIntervalMilliseconds` et `HeartbeatTimeoutMilliseconds` ;
- synchronisation temporelle : `SmoothServerTimeOffset`, `ServerTimeOffsetSmoothingSpeed`, `TimeSynchronizationRequestTimeoutMilliseconds` et `TimeSynchronizationMaxAttempts` ;
- synchronisation du monde : `SynchronizationTransport`, `MaxStoredSynchronizationFrames` et `AutoSendSynchronizationFrames` ;
- charge et arrêt : `MaxQueuedInboundMessages` et `MessageWorkerStopTimeoutMilliseconds`.

La configuration est validée lors de l’initialisation, de `Save()` et de son application au client. Une valeur invalide produit une `InvalidOperationException` avant la connexion.

### Modifier et appliquer la configuration

```csharp
NetSquareClientConfiguration configuration =
    NetSquareClientConfigurationManager
        .Get<NetSquareClientConfiguration>();

configuration.Host = "game.example.com";
configuration.Port = 5555;
configuration.UseTLS = true;
configuration.TLSServerName = "game.example.com";

NetSquareClientConfigurationManager.Save();
client.ApplyConfiguration(configuration);
```

`ApplyConfiguration()` est uniquement autorisé lorsque le client est déconnecté et qu’aucune tentative de connexion n’est active. `Connect()` et `ConnectAsync()` réappliquent également l’objet `Configuration` courant avant de se connecter.

### Ajouter des réglages propres au projet

```csharp
public sealed class GameClientConfiguration
    : NetSquareClientConfiguration
{
    public string Region { get; set; } = "eu-west";
    public string BuildChannel { get; set; } = "stable";
}

NetSquareClientConfigurationManager
    .Initialize<GameClientConfiguration>();

GameClientConfiguration configuration =
    NetSquareClientConfigurationManager
        .Get<GameClientConfiguration>();

NetSquareClient client = new NetSquareClient(configuration);
```

Les propriétés supplémentaires sont chargées et sauvegardées dans le même fichier JSON que les réglages NetSquare.

## Configuration serveur

### Initialisation et démarrage

```csharp
using NetSquare.Core;
using NetSquare.Server;

NetSquareConfigurationManager
    .Initialize<NetSquareConfiguration>();

NetSquareConfiguration configuration =
    NetSquareConfigurationManager
        .Get<NetSquareConfiguration>();

NetSquareServer server =
    new NetSquareServer(NetSquareProtocoleType.TCP_AND_UDP);

server.Start();
```

L’initialisation doit impérativement précéder `new NetSquareServer(...)`. Le constructeur lit notamment TLS, l’authentification UDP et les paramètres des workers.

`server.Start()` utilise `configuration.Port`. Un port positif passé à `server.Start(port)` remplace cette valeur pour ce démarrage.

Un chemin personnalisé peut être fourni :

```csharp
NetSquareConfigurationManager
    .Initialize<NetSquareConfiguration>(
        @"Configuration\server.production.json");
```

Les principaux groupes de réglages serveur sont :

- écoute et sécurité : `Port`, `UseTLS`, `TLSCertificatePath`, `TLSCertificatePassword` et `UseUdpAuthentication` ;
- traitement : `NbQueueThreads`, `MessageQueueCapacity`, `NbSendingThreads`, `ReceivingBufferSize` et `WorkerStopTimeoutMilliseconds` ;
- boucle et synchronisation : `UpdateFrequencyHz` et `SynchronizingFrequency` ;
- console : `LockConsole` ;
- blacklist, politiques de hits, persistance et réputation externe : les propriétés préfixées par `BlackList`, `AbuseIPDB`, `BlockListDe`, `Spamhaus` et `DShield`.

Le fonctionnement détaillé de la blacklist est décrit dans [NetSquareServer/BLACKLIST.md](NetSquareServer/BLACKLIST.md).

Les jetons `[current]` présents dans `BlackListFilePath` et `TLSCertificatePath` sont remplacés par le répertoire de travail courant lors de l’initialisation.

### Modifier et sauvegarder

```csharp
NetSquareConfiguration configuration =
    NetSquareConfigurationManager
        .Get<NetSquareConfiguration>();

configuration.Port = 5555;
configuration.NbQueueThreads = 2;
configuration.UpdateFrequencyHz = 30;

NetSquareConfigurationManager.Save();
```

Configurez de préférence le serveur avant sa construction. Plusieurs réglages sont capturés par les services au démarrage ; sauvegarder une nouvelle valeur ne reconfigure donc pas automatiquement un serveur déjà actif.

### Ajouter des réglages propres au projet

```csharp
public sealed class GameServerConfiguration
    : NetSquareConfiguration
{
    public int MaxPlayers { get; set; } = 100;
    public string DatabaseHost { get; set; } = "localhost";
}

NetSquareConfigurationManager
    .Initialize<GameServerConfiguration>();

GameServerConfiguration configuration =
    NetSquareConfigurationManager
        .Get<GameServerConfiguration>();

configuration.MaxPlayers = 250;
NetSquareConfigurationManager.Save();

NetSquareServer server = new NetSquareServer();
server.Start();
```

Une configuration personnalisée doit être publique, dériver du type client ou serveur correspondant et avoir un constructeur public sans paramètre.

## API des gestionnaires

| Méthode | Rôle |
|---|---|
| `Initialize<T>(string filePath = null)` | Sélectionne le type concret, charge le JSON ou crée le fichier avec les valeurs par défaut. |
| `Get<T>()` | Retourne l’instance active sous son type concret ou un type de base compatible. |
| `Save()` | Enregistre l’instance active dans le fichier JSON. Le gestionnaire client effectue aussi sa validation. |

Les gestionnaires synchronisent leurs propres opérations, mais l’objet de configuration retourné reste mutable. Effectuez donc de préférence les modifications pendant la phase d’initialisation, avant de démarrer les threads réseau.

## Fichiers sensibles

Les mots de passe, clés API et autres secrets sont enregistrés en clair dans le JSON. Ne versionnez pas les fichiers de production et limitez leurs droits d’accès.

Lors de l’affichage de la configuration serveur dans les logs, les propriétés dont le nom contient `ApiKey`, `Password`, `Secret` ou `Token` sont masquées. Cela ne chiffre pas leur valeur dans le fichier.
