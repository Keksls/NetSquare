# Blacklist générique et escalade des sanctions

Le moteur de blacklist peut cibler n’importe quelle identité stable. NetSquare connaît automatiquement les adresses IP, tandis que le projet consommateur fournit les identifiants de comptes, appareils ou autres entités.

## 1. Définir une politique

Une politique contient une fenêtre de hits et plusieurs paliers de sanction :

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
NetSquareConfigurationManager.Save();
```

Le scénario obtenu est :

```text
15 hits → ban 15 minutes
 5 hits → ban 30 minutes
 5 hits → ban 1 heure
 5 hits → ban permanent
```

`EscalationResetAfterSeconds = 0` conserve le palier indéfiniment. Une valeur positive remet le sujet au premier palier après cette durée sans incident.

Si `BlackListPolicies` est vide, NetSquare utilise les anciennes propriétés scalaires (`BlackListHitThreshold`, `BlackListHitWindowSeconds`, etc.) comme une politique à un seul palier.

## 2. Ajouter un hit à un compte

NetSquare ne connaît pas les comptes. Le projet crée donc un sujet après avoir authentifié l’utilisateur :

```csharp
BlackListSubject account =
    new BlackListSubject("account", accountId.ToString());

BlackListHitResult result = BlackListManager.AddHit(
    account,
    hitCount: 1,
    reason: "Flood du chat");

if (result.IsBanned)
{
    server.DisconnectClient(
        clientId,
        result.CreateDisconnectInfo());
}
```

Le type du sujet est insensible à la casse. Son identifiant est sensible à la casse : le projet doit donc toujours fournir une représentation canonique et stable.

Une politique portant le même nom que le type du sujet est sélectionnée automatiquement. Il est aussi possible de forcer une politique :

```csharp
BlackListManager.AddHit(
    account,
    reason: "Tentative d'exploitation",
    policyName: "high-risk");
```

## 3. Utiliser une adresse IP

```csharp
BlackListSubject ip =
    BlackListSubject.ForIPAddress("203.0.113.10");

BlackListManager.AddHit(ip, reason: "Paquet invalide");
```

Les anciennes surcharges IP restent disponibles :

```csharp
BlackListManager.AddHit("203.0.113.10");
BlackListManager.AddHit(client, reason: "Handshake invalide");
BlackListManager.BanIP("203.0.113.10", BlackListBanType.Permanent);
```

`AddHit(ConnectedClient)` envoie automatiquement la raison typée au client avant de fermer sa socket. AbuseIPDB, BlockList.de, Spamhaus et DShield ne s’appliquent qu’aux sujets IP.

## 4. Administrer un sujet

```csharp
BlackListStatus status = BlackListManager.GetStatus(account);

BlackListManager.Ban(
    account,
    BlackListBanType.Temporary,
    TimeSpan.FromMinutes(30),
    "Sanction manuelle");

BlackListManager.Unban(account);
BlackListManager.ClearHits(account);
BlackListManager.ClearHistory(account);
```

- `Unban` retire uniquement le ban actif et conserve le palier atteint.
- `ClearHits` efface les hits de la fenêtre actuelle.
- `ClearHistory` remet les hits et l’escalade à zéro, sans supprimer un ban encore actif.
- `GetStatus` retourne le palier, les hits, la prochaine limite et le ban éventuel.

## 5. Persistance

Le fichier défini par `BlackListFilePath` contient les sujets et leur état :

- les bans permanents et l’historique d’escalade sont toujours persistés ;
- `BlackListPersistTemporaryBans` contrôle la persistance des bans temporaires ;
- `BlackListPersistHitProgress` contrôle la persistance des hits et de leur fenêtre ;
- les anciens fichiers contenant uniquement des IP sont migrés automatiquement.

Après l’expiration d’un ban temporaire, le sujet conserve donc son palier et utilise le seuil du palier suivant.
