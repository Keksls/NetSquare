# Writer et système de logs

`Writer` est le système de sortie console et de journalisation de `NetSquare.Server`.

Il fournit :

- quatre niveaux de sévérité ;
- des catégories déclarables par NetSquare ou par les projets consommateurs ;
- des filtres indépendants pour la console et le fichier de log ;
- des événements structurés avec propriétés métier ;
- une file asynchrone bornée et non bloquante ;
- des messages interpolés sans allocation sur les types courants ;
- plusieurs sorties : console, delegate, WinForms ou sortie personnalisée.

Le namespace public est :

```csharp
using NetSquare.Server.Utils;
```

## Niveaux

| API | Niveau | Valeur | Couleur par défaut |
|---|---:|---:|---|
| `Writer.Write(...)` | `Message` | 0 | Blanc |
| `Writer.Info(...)` | `Information` | 1 | Cyan |
| `Writer.Warning(...)` | `Warning` | 2 | Jaune |
| `Writer.Error(...)` | `Error` | 3 | Rouge |

Les seuils sont inclusifs. Un seuil `Warning` accepte `Warning` et `Error`.

## Utilisation simple

```csharp
Writer.Write("Server starting");
Writer.Info("Server started");
Writer.Warning("Queue is almost full");
Writer.Error("Server failed to start");
```

Avec interpolation :

```csharp
uint clientId = 42;
int port = 5555;

Writer.Info($"Client {clientId} connected on port {port}");
```

Avec exception :

```csharp
try
{
    StartDatabase();
}
catch (Exception exception)
{
    Writer.Error("Database startup failed", exception);
}
```

## Catégories

Une catégorie identifie l’origine fonctionnelle d’un log. Les projets utilisant NetSquare peuvent déclarer leurs propres catégories.

Il est recommandé de les centraliser :

```csharp
public static class GameLogCategories
{
    public static readonly WriterCategory General =
        Writer.DefineCategory("Game");

    public static readonly WriterCategory Authentication =
        Writer.DefineCategory("Game.Authentication");

    public static readonly WriterCategory Economy =
        Writer.DefineCategory("Game.Economy");

    public static readonly WriterCategory Trading =
        Writer.DefineCategory("Game.Economy.Trading");
}
```

`DefineCategory` :

- retourne toujours la même instance pour un même nom ;
- compare les noms sans tenir compte de la casse ;
- supprime les espaces au début et à la fin ;
- n’accepte pas un nom vide ;
- ne démarre pas le worker de logs.

Utilisation :

```csharp
Writer.Info(GameLogCategories.Authentication, $"Client {clientId} authenticated");
Writer.Warning(GameLogCategories.Economy, "Economy service is degraded");
Writer.Error(GameLogCategories.Trading, exception, $"Trade {tradeId} failed");
```

### Catégories NetSquare intégrées

```csharp
NetSquareLogCategories.General
NetSquareLogCategories.Database
NetSquareLogCategories.PhysicalPersistence
NetSquareLogCategories.Spells
NetSquareLogCategories.Monsters
NetSquareLogCategories.Fight
NetSquareLogCategories.Server
NetSquareLogCategories.Pnj
NetSquareLogCategories.Logging
```

### Hiérarchie

Le point sépare les niveaux d’une hiérarchie :

```text
Game
Game.Economy
Game.Economy.Trading
```

Une règle appliquée à `Game.Economy` affecte également `Game.Economy.Trading`.

La règle la plus précise gagne. Une règle sur `Game.Economy.Trading` remplace donc celle de `Game.Economy` pour cette branche.

## Configuration des filtres

Les seuils globaux par défaut sont :

```csharp
Writer.MinimumConsoleLevel = NetSquareLogLevel.Message;
Writer.MinimumLogLevel = NetSquareLogLevel.Message;
```

`MinimumConsoleLevel` contrôle la sortie console ou la sortie personnalisée.

`MinimumLogLevel` contrôle le fichier créé par `StartRecordingLog`.

Configuration d’une catégorie :

```csharp
Writer.ConfigureCategory(
    GameLogCategories.Economy,
    consoleMinimumLevel: NetSquareLogLevel.Warning,
    logMinimumLevel: NetSquareLogLevel.Information);
```

La même configuration peut être déclarée avant la création de la catégorie :

```csharp
Writer.ConfigureCategory(
    "Game.Network",
    consoleMinimumLevel: NetSquareLogLevel.Error,
    logMinimumLevel: NetSquareLogLevel.Information);

WriterCategory network = Writer.DefineCategory("Game.Network.Tcp");
```

Une valeur `null` désactive complètement la destination concernée :

```csharp
Writer.ConfigureCategory(
    "Game.Verbose",
    consoleMinimumLevel: null,
    logMinimumLevel: NetSquareLogLevel.Message);
```

Ici, `Game.Verbose` est écrit dans le fichier mais jamais affiché dans la console.

Pour supprimer une règle :

```csharp
bool removed = Writer.ResetCategoryConfiguration("Game.Economy");
```

La catégorie retrouve alors la règle de son parent ou les seuils globaux.

Pour tester un niveau avant un calcul coûteux :

```csharp
if (Writer.IsEnabled(GameLogCategories.Economy, NetSquareLogLevel.Information))
{
    string report = BuildExpensiveEconomyReport();
    Writer.Info(GameLogCategories.Economy, report);
}
```

Pour obtenir un snapshot des catégories enregistrées :

```csharp
WriterCategory[] categories = Writer.GetCategories();
```

`GetCategories()` alloue un tableau et doit rester une API de configuration ou de diagnostic.

## Messages interpolés et allocations

Les appels interpolés utilisent automatiquement les handlers de `Writer` :

```csharp
Writer.Write($"Tick {tick}");
Writer.Info(GameLogCategories.General, $"Player {playerId} joined");
Writer.Warning(GameLogCategories.Economy, $"Balance is low: {balance}");
Writer.Error(GameLogCategories.Trading, exception, $"Trade {tradeId} failed");
```

Si la catégorie ou le niveau est filtré, le buffer n’est pas loué et les expressions interpolées ne sont pas évaluées.

Les types suivants utilisent le chemin sans allocation :

- `string` ;
- `char` ;
- `bool` ;
- `sbyte`, `byte` ;
- `short`, `ushort` ;
- `int`, `uint` ;
- `long`, `ulong`.

Les autres types utilisent un chemin compatible basé sur `ToString` ou `IFormattable`. Ce chemin peut allouer, notamment pour :

- `float`, `double`, `decimal` dans une interpolation ;
- `Guid` et `DateTime` dans une interpolation ;
- les objets personnalisés ;
- les formats ou alignements personnalisés.

Les handlers ne doivent jamais être instanciés manuellement.

Pour profiter de cette optimisation, utiliser directement une interpolation :

```csharp
Writer.Info(category, $"Client {clientId} connected");
```

Éviter la concaténation, qui crée la chaîne avant l’appel :

```csharp
Writer.Info(category, "Client " + clientId + " connected");
```

Les overloads `Write` qui précisent une couleur ou le comportement de nouvelle ligne prennent une `string`. Une interpolation passée à ces overloads est donc construite avant l’appel :

```csharp
Writer.Write($"Client {clientId}", ConsoleColor.Green, true);
```

## Événements structurés et données métier

Un événement structuré possède :

- une catégorie ;
- un niveau ;
- un nom d’événement stable ;
- un message lisible ;
- éventuellement une exception ;
- des propriétés métier nommées.

### Information

```csharp
Writer.Info(
    GameLogCategories.Economy,
    "OrderPaid",
    "An order was paid",
    new NetSquareLogProperty("OrderId", orderId),
    new NetSquareLogProperty("PlayerId", playerId),
    new NetSquareLogProperty("Amount", amount),
    new NetSquareLogProperty("Currency", currency));
```

### Warning

```csharp
Writer.Warning(
    GameLogCategories.Authentication,
    "LoginRateLimited",
    "A login attempt was rate limited",
    new NetSquareLogProperty("AccountId", accountId),
    new NetSquareLogProperty("Address", address));
```

### Error

```csharp
Writer.Error(
    GameLogCategories.Trading,
    "TradeFailed",
    "A trade could not be completed",
    exception,
    new NetSquareLogProperty("TradeId", tradeId),
    new NetSquareLogProperty("SellerId", sellerId),
    new NetSquareLogProperty("BuyerId", buyerId));
```

### Niveau dynamique

```csharp
Writer.Log(
    GameLogCategories.General,
    NetSquareLogLevel.Warning,
    "CustomEvent",
    "A custom event occurred",
    exception: null,
    new NetSquareLogProperty("Value", value));
```

### Types de propriétés

`NetSquareLogProperty` fournit des constructeurs spécialisés pour :

- `string` ;
- `int`, `uint` ;
- `long`, `ulong` ;
- `double` ;
- `decimal` ;
- `bool` ;
- `Guid` ;
- `DateTime` ;
- `object` comme fallback.

Les constructeurs spécialisés évitent le boxing. Le constructeur `object` peut boxer les types valeur.

```csharp
new NetSquareLogProperty("PlayerId", playerId);
new NetSquareLogProperty("Success", true);
new NetSquareLogProperty("Amount", 19.95m);
new NetSquareLogProperty("SessionId", sessionId);
new NetSquareLogProperty("Timestamp", DateTime.UtcNow);
```

Le nom d’une propriété ne peut pas être vide.

Les propriétés sont conservées dans un tableau `params`. Il faut donc :

- réserver les événements structurés aux données réellement utiles ;
- ne pas modifier le tableau ou ses valeurs après l’appel, car le traitement est asynchrone ;
- utiliser `IsEnabled` avant de créer des propriétés coûteuses lorsque le niveau peut être filtré.

Le nom d’événement et les propriétés sont écrits dans le fichier. La sortie console reçoit seulement le préfixe de catégorie, le message et l’exception. Le message doit donc rester compréhensible sans ses propriétés.

## Sortie console

La console est activée par défaut.

```csharp
Writer.StartDisplayLog();
Writer.StopDisplayLog();
```

`StopDisplayLog()` n’arrête pas l’écriture fichier.

Restaurer la console standard :

```csharp
Writer.SetOutputAsConsole();
```

Désactiver la sortie console tout en conservant le fichier :

```csharp
Writer.SetOutputAsNull();
```

Pour une catégorie différente de `Writer.DefaultCategory`, la sortie affiche un préfixe :

```text
[Game.Economy] An order was paid
```

La catégorie par défaut `NetSquare` n’affiche pas de préfixe dans la console.

## Sortie personnalisée

Une sortie personnalisée implémente :

```csharp
public interface INetSquareWriterOutput
{
    void Write(string text, ConsoleColor color, bool appendNewLine);
    void SetTitle(string text);
}
```

Enregistrement :

```csharp
Writer.SetOutput(new MyWriterOutput());
```

Ou avec des delegates :

```csharp
Writer.SetOutput(
    (text, color, appendNewLine) => MyConsole.Write(text, color, appendNewLine),
    title => MyConsole.SetTitle(title));
```

La méthode `Write` de la sortie est appelée par l’unique worker de logs. Elle doit rester rapide et ne doit pas rappeler `Writer`, afin d’éviter une boucle récursive.

Une sortie publique personnalisée reçoit les messages interpolés sous forme de `string`. La conversion éventuelle est effectuée par le worker, pas par le thread serveur.

Les exceptions levées par une sortie sont ignorées par `Writer` afin de ne pas arrêter le serveur.

Sauvegarde et restauration d’une sortie :

```csharp
INetSquareWriterOutput previousOutput = Writer.GetOutput();
Writer.SetOutputAsNull();

// Travail sans affichage console.

Writer.SetOutput(previousOutput);
```

Passer `null` à `SetOutput(INetSquareWriterOutput)` restaure la console standard. Pour désactiver la sortie, utiliser `SetOutputAsNull()`.

## WinForms

```csharp
Writer.SetOutputAsRichTextBox(richTextBox);
Writer.SetOutputAsTextBox(textBox);
```

Les écritures sont transférées avec `BeginInvoke` vers le thread de l’interface.

`RichTextBox` conserve les couleurs. Un `TextBoxBase` reçoit du texte simple.

## Titre

```csharp
Writer.Title("My Game Server");
Writer.StartDisplayTitle();
Writer.StopDisplayTitle();
```

Le titre est envoyé directement à la sortie actuelle et ne passe pas par la file de logs.

## Fichier de log

Démarrage avec le chemin par défaut :

```csharp
Writer.StartRecordingLog();
```

Le fichier par défaut est :

```text
<répertoire courant>/server.log
```

Chemin personnalisé :

```csharp
Writer.StartRecordingLog("logs/game-server.log");
```

Au démarrage :

1. le dossier est créé si nécessaire ;
2. l’ancien fichier `_prev` est supprimé ;
3. le fichier courant est déplacé vers `<nom>_prev.<extension>` ;
4. un nouveau fichier est créé ;
5. un événement `LogStarted` est enregistré.

Un second appel pendant qu’un fichier est déjà ouvert lève `InvalidOperationException`.

Arrêt du fichier :

```csharp
Writer.StopRecordingLog();
```

Cette méthode désactive les nouvelles destinations fichier, vide la file puis ferme le flux.

### Format

```text
[2026-07-19T12:34:56.1234567Z] [Information] [Game.Economy] [OrderPaid] An order was paid | OrderId=42 | Amount=19.95 | Currency=EUR
```

Une exception est écrite sur la ligne suivante avec sa stack trace.

Les timestamps sont en UTC au format ISO 8601 avec sept chiffres de précision.

Les couleurs console ne sont pas écrites dans le fichier.

## Configuration des performances

Ces propriétés doivent être configurées avant le premier log accepté ou avant `StartRecordingLog()` :

```csharp
Writer.QueueCapacity = 8192;
Writer.MessageBufferSize = 512;
Writer.FlushIntervalMilliseconds = 1000;
```

### `QueueCapacity`

- valeur par défaut : `8192` ;
- doit être une puissance de deux ;
- doit être strictement supérieure à `64` ;
- ne peut plus être modifiée après le démarrage du worker.

La file est bornée. Un producteur n’attend jamais qu’une place se libère.

Une réserve est conservée pour `Warning` et `Error`. Les niveaux `Message` et `Information` peuvent donc être rejetés avant que la file soit entièrement pleine.

### `MessageBufferSize`

- valeur par défaut : `512` caractères ;
- valeur minimale : `64` ;
- ne peut plus être modifiée après le démarrage du worker.

Cette taille concerne les messages interpolés. Un message trop long est tronqué et terminé par `...`.

Les messages passés comme chaînes existantes ne sont pas tronqués par ce buffer.

### `FlushIntervalMilliseconds`

- valeur par défaut : `1000` ms ;
- doit être strictement positive ;
- peut être modifiée pendant l’exécution.

Le flush périodique est exécuté par le worker.

### Mémoire préallouée

Au premier log accepté, `Writer` crée :

- le ring buffer de la file ;
- le pool de buffers de messages ;
- le thread worker.

Le pool de caractères réserve approximativement :

```text
QueueCapacity × MessageBufferSize × 2 octets
```

Avec les valeurs par défaut, cela représente environ 8 Mio pour les caractères.

## Saturation et diagnostic

```csharp
long dropped = Writer.DroppedLogCount;
long truncated = Writer.TruncatedLogCount;
```

`DroppedLogCount` compte les entrées rejetées lorsque :

- la file est pleine ;
- la réserve des niveaux élevés est atteinte ;
- aucun buffer interpolé n’est disponible.

`TruncatedLogCount` compte les messages interpolés tronqués.

Les compteurs sont cumulatifs et ne possèdent pas d’API de remise à zéro.

Le worker essaie également d’émettre un avertissement agrégé dans la catégorie `NetSquare.Logging` après des pertes.

## Flush et arrêt

Vider explicitement la file :

```csharp
bool drained = Writer.Flush(timeoutMilliseconds: 5000);
```

`Flush` :

- attend que la file soit vide ;
- flush le fichier actif ;
- retourne `true` si la file a été vidée avant le timeout ;
- n’arrête pas le worker ;
- accepte uniquement un timeout positif ou nul.

Arrêt définitif :

```csharp
bool drained = Writer.Shutdown(timeoutMilliseconds: 5000);
```

`Shutdown` :

- refuse les nouveaux logs ;
- vide la file ;
- arrête le worker ;
- flush et ferme le fichier ;
- est idempotent.

`Writer` ne peut pas être redémarré après `Shutdown`. Cette méthode doit être appelée uniquement par l’application hôte lors de son arrêt, jamais par une bibliothèque.

Un `Shutdown(5000)` est automatiquement demandé lors de `ProcessExit`.

## Threading

Les appels de log sont thread-safe.

Le chemin producteur :

- filtre le niveau et la catégorie ;
- loue éventuellement un buffer préalloué ;
- ajoute une structure dans le ring buffer ;
- ne fait aucune entrée/sortie ;
- ne bloque pas lorsque la file est saturée.

Le worker unique effectue :

- l’affichage console ;
- l’appel de la sortie personnalisée ;
- le formatage fichier ;
- l’écriture et les flushs fichier ;
- le formatage des exceptions.

Les destinations sont déterminées au moment de l’appel. Une entrée déjà en file conserve ses destinations même si les filtres changent ensuite.

## API `Write` et nouvelle ligne

```csharp
Writer.Write(string text, ConsoleColor color, bool inline = true);
Writer.Write(string text, bool inline = true);
Writer.Write(WriterCategory category, string text, ConsoleColor color = ConsoleColor.White, bool inline = true);
```

Pour compatibilité historique, le paramètre s’appelle `inline`, mais sa valeur est utilisée comme `appendNewLine` :

- `inline: true` ajoute une nouvelle ligne ;
- `inline: false` n’ajoute pas de nouvelle ligne.

Exemple :

```csharp
Writer.Write("Starting server...", ConsoleColor.Yellow, inline: false);
Writer.Write("OK", ConsoleColor.Green, inline: true);
```

Les overloads interpolés simples ajoutent toujours une nouvelle ligne.

## Helpers historiques

Les helpers suivants sont conservés pour compatibilité :

```csharp
Writer.Write_Database(...);
Writer.Write_Physical(...);
Writer.Write_Spells(...);
Writer.Write_Monsters(...);
Writer.Write_Fight(...);
Writer.Write_Server(...);
Writer.Write_PNJ(...);
```

Ils utilisent désormais les catégories NetSquare correspondantes.

Les helpers sans argument écrivent uniquement un ancien préfixe texte :

```csharp
Writer.Database();
Writer.Physical();
Writer.Spells();
Writer.Monsters();
Writer.Fight();
Writer.Server();
Writer.PNJ();
```

Pour du nouveau code, préférer une catégorie explicite et `Write`, `Info`, `Warning` ou `Error`.

## Initialisation recommandée

```csharp
public static void ConfigureLogging()
{
    // Ces valeurs doivent être définies avant le premier log accepté.
    Writer.QueueCapacity = 8192;
    Writer.MessageBufferSize = 512;
    Writer.FlushIntervalMilliseconds = 1000;

    Writer.MinimumConsoleLevel = NetSquareLogLevel.Information;
    Writer.MinimumLogLevel = NetSquareLogLevel.Message;

    Writer.ConfigureCategory(
        "Game.Network.Verbose",
        consoleMinimumLevel: null,
        logMinimumLevel: NetSquareLogLevel.Message);

    Writer.ConfigureCategory(
        "Game.Economy",
        consoleMinimumLevel: NetSquareLogLevel.Warning,
        logMinimumLevel: NetSquareLogLevel.Information);

    Writer.SetOutputAsConsole();
    Writer.StartDisplayLog();
    Writer.StartRecordingLog("logs/game-server.log");
}
```

Arrêt recommandé :

```csharp
public static void StopLogging()
{
    Writer.Shutdown(5000);
}
```

## Référence rapide

```csharp
WriterCategory category = Writer.DefineCategory("Project.Domain");

Writer.Write("message");
Writer.Write(category, "message");
Writer.Info("information");
Writer.Info(category, "information");
Writer.Warning("warning");
Writer.Warning(category, "warning");
Writer.Error("error", exception);
Writer.Error(category, "error", exception);

Writer.Info(category, "EventName", "message", properties);
Writer.Warning(category, "EventName", "message", properties);
Writer.Error(category, "EventName", "message", exception, properties);
Writer.Log(category, level, "EventName", "message", exception, properties);

Writer.ConfigureCategory(category, consoleLevel, fileLevel);
Writer.ConfigureCategory("Project.Domain", consoleLevel, fileLevel);
Writer.ResetCategoryConfiguration("Project.Domain");
Writer.IsEnabled(category, level);
Writer.GetCategories();

Writer.StartDisplayLog();
Writer.StopDisplayLog();
Writer.StartRecordingLog(path);
Writer.StopRecordingLog();
Writer.Flush(timeout);
Writer.Shutdown(timeout);
```

