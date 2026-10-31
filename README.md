# Dofus Bank Manager

Lit votre banque Dofus Retro en écoutant passivement le trafic réseau, puis liste les recettes que vous pouvez fabriquer.

> **Avant de modifier le code, lisez les [règles de développement](#règles-de-développement).**
> Elles ne sont pas négociables : elles garantissent que l'application reste un simple outil de lecture.

## Lancement

Double-cliquez sur `Lancer DofusBankManager.bat`. L'application a besoin des droits administrateur pour lire le trafic réseau.

Ensuite, en jeu, **ouvrez votre banque** : elle est capturée, puis sauvegardée. Elle reste disponible au lancement suivant.

Pour recompiler (.NET 10 SDK requis) :

```
dotnet publish DofusBankManager -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Règles de développement

### 1. Lecture seule, toujours

L'application **observe**. Elle n'agit jamais sur le jeu.

- **Ne jamais envoyer de paquet** au serveur Dofus, ni en ouvrir une connexion.
- **Ne jamais modifier, bloquer ou rejouer** un paquet. Pas de proxy, pas de redirection du client (pas de modification du `config.xml`, du fichier `hosts`, etc.).
- La capture reste passive : une socket brute en réception (`SIO_RCVALL`), et rien d'autre.

### 2. Ne pas toucher au client

- **Pas de lecture ni d'écriture de la mémoire** du processus Dofus.
- **Pas d'injection** de code ou de DLL, pas de hook.
- **Pas de modification des fichiers** installés du jeu (`%LOCALAPPDATA%\Ankama\Retro`). Les lire, comme le `config.xml`, est autorisé.

### 3. Aucune automatisation en jeu

- **Pas de clics, de touches ou de déplacements simulés**, pas de macro.
- L'application **affiche** des informations. C'est toujours le joueur qui agit, à la main.
- Une fonctionnalité qui ferait jouer à la place du joueur (crafter, déposer, vendre automatiquement) est **refusée**, même « juste pour tester ».

C'est ce qui sépare un outil d'aide d'un bot. Les sanctions d'Ankama visent surtout cette catégorie.

### 4. Protéger les données du joueur

Le trafic capturé contient des informations sensibles : nom de compte, ticket de connexion, messages privés.

- **Ne lire que les messages utiles** à la banque et à l'inventaire (voir [DofusTracker.cs](DofusBankManager/Net/DofusTracker.cs)). Les autres sont ignorés.
- **Rien ne sort de la machine** : pas d'envoi vers un serveur, pas de télémétrie.
- `packets.log` est un outil de diagnostic. Il **ne doit contenir que les messages de banque et d'inventaire** (`ECK`, `EL`, `EsK`, `EV`, `OAKO`, `OR`, `OQ`), filtrés par `DofusTracker.IsLoggable`. **Ne jamais élargir ce filtre** à des messages de connexion, de chat ou d'identité (`ASK` compris). Ne committez jamais ce fichier.
- La journalisation des paquets reste **désactivée par défaut**, et elle n'écrit pas les adresses IP.

### 5. Données du jeu

- Les fichiers de langue (objets, recettes, métiers) appartiennent à Ankama. Ils sont **téléchargés depuis leur CDN et mis en cache localement**. **Ne pas les committer ni les redistribuer.**
- Rester sobre : un fichier n'est téléchargé que si sa version a changé (voir [GameData.cs](DofusBankManager/Data/GameData.cs)). Pas de requêtes en boucle.

### 6. Rappel

Même passif, cet outil est un logiciel tiers. Les conditions d'utilisation d'Ankama peuvent l'interdire, et aucune technique ne garantit l'absence de détection. **Chacun l'utilise à ses propres risques.** Respecter les règles ci-dessus réduit le risque, sans l'annuler.

**En cas de doute sur une fonctionnalité, la réponse par défaut est non.**

## Fonctionnement

- **Capture** ([Net/PacketSniffer.cs](DofusBankManager/Net/PacketSniffer.cs)) : socket brute Windows (`SIO_RCVALL`), sans Npcap. Les flux TCP sont réassemblés, puis découpés en messages sur `\0`.
- **Protocole** ([Net/DofusTracker.cs](DofusBankManager/Net/DofusTracker.cs)) : `ECK5` ouvre la banque, `EL…` donne son contenu, `EsK…` ses mouvements. `ASK` / `OAKO` / `OR` / `OQ` suivent l'inventaire.
- **Données du jeu** ([Data/GameData.cs](DofusBankManager/Data/GameData.cs)) : les fichiers `items`, `crafts`, `skills` et `jobs` sont décodés par un mini-interpréteur AS2 ([Data/Avm1.cs](DofusBankManager/Data/Avm1.cs)).

Les fichiers sont stockés dans `%LOCALAPPDATA%\DofusBankManager` : cache des langues, banques sauvegardées et `packets.log`.

## Dépannage

Si la banque n'apparaît pas :

1. Cochez « Journaliser les paquets ».
2. Ouvrez la banque en jeu.
3. Lisez `%LOCALAPPDATA%\DofusBankManager\packets.log`. Il ne contient que les messages de banque et d'inventaire (règle 4).
