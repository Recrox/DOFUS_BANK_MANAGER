# Dofus Bank Manager

Lit votre banque Dofus Retro en écoutant passivement le trafic réseau, puis liste les recettes que vous pouvez fabriquer.

> **Avant de modifier le code, lisez les [règles de développement](#règles-de-développement).**
> Elles ne sont pas négociables : elles garantissent que l'application reste un simple outil de lecture.

## Installation (pour un utilisateur)

1. Téléchargez `DofusBankManager-Setup.msi` depuis les [Releases](https://github.com/Recrox/DOFUS_BANK_MANAGER/releases/latest), puis lancez-le.
2. Acceptez les conditions d'utilisation, puis choisissez le dossier d'installation.
3. À la fin, laissez cochée la case **« Lancer Dofus Bank Manager »** si vous voulez l'ouvrir tout de suite, puis acceptez la demande de droits administrateur.

Ensuite, l'application s'ouvre depuis le menu Démarrer (dossier « Dofus Bank Manager ») ou le Bureau.

Aucun prérequis : .NET est inclus. L'installateur ajoute la règle de pare-feu « Dofus Bank Manager ».

L'installateur n'est pas signé numériquement : Windows SmartScreen affichera « Windows a protégé votre ordinateur ». Cliquez sur **Informations complémentaires**, puis **Exécuter quand même**.

### Mises à jour

Au lancement, l'application vérifie si une version plus récente est publiée dans les Releases GitHub. Si c'est le cas, un bandeau propose de la télécharger. Il suffit de lancer le nouvel installateur : il remplace l'ancienne version et conserve les banques sauvegardées et le thème.

### Désinstallation

Deux possibilités :

- Menu Démarrer › Dofus Bank Manager › **Désinstaller Dofus Bank Manager** ;
- Paramètres Windows › Applications › Dofus Bank Manager › **Désinstaller**.

La désinstallation supprime l'application, les raccourcis et la règle de pare-feu. Vos données personnelles (banques sauvegardées, thème, cache des données du jeu) restent dans `%LOCALAPPDATA%\DofusBankManager`, au cas où vous réinstalleriez. Supprimez ce dossier à la main pour tout effacer.

### Construire l'installateur

```
.\build-installer.ps1
```

Le fichier est créé dans `dist\DofusBankManager-Setup.msi`. WiX Toolset est récupéré automatiquement via NuGet : rien à installer.

### Publier une nouvelle version

1. Augmentez `<Version>` dans [DofusBankManager.csproj](DofusBankManager/DofusBankManager.csproj), par exemple `1.1.0`.
2. Lancez `.\build-installer.ps1`.
3. Sur GitHub, créez une Release avec le tag **`v1.1.0`** (le même numéro, précédé de `v`), et joignez-y `dist\DofusBankManager-Setup.msi`.

Les applications déjà installées compareront leur version au tag de la dernière Release et afficheront le bandeau de mise à jour. Ne changez jamais l'`UpgradeCode` de [installer/Package.wxs](installer/Package.wxs) : c'est lui qui permet au nouvel installateur de remplacer l'ancien.

## Lancement (développement)

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
- **Aucune donnée du joueur ne sort de la machine** : pas de télémétrie, pas d'envoi de la banque ni de l'inventaire. Les seules requêtes réseau autorisées sont :
  - le téléchargement des données du jeu depuis le CDN d'Ankama ([GameData.cs](DofusBankManager/Data/GameData.cs)) ;
  - la lecture de la dernière Release GitHub du projet pour vérifier la version ([UpdateChecker.cs](DofusBankManager/UpdateChecker.cs)).

  Ces requêtes ne contiennent aucune information sur le joueur. Toute nouvelle requête vers l'extérieur doit respecter la même règle.
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
- **Pare-feu** ([Net/FirewallRule.cs](DofusBankManager/Net/FirewallRule.cs)) : sans autorisation, Windows bloque le trafic entrant vers une socket brute, et seuls les messages envoyés par le client sont visibles. Au démarrage de la capture, l'appli ajoute donc la règle entrante « Dofus Bank Manager », limitée à son exécutable. Pour la retirer : `netsh advfirewall firewall delete rule name="Dofus Bank Manager"`.
- **Protocole** ([Net/DofusTracker.cs](DofusBankManager/Net/DofusTracker.cs)) : `ECK5` ouvre la banque, `EL…` donne son contenu, `EsK…` ses mouvements. `ASK` / `OAKO` / `OR` / `OQ` suivent l'inventaire.
- **Données du jeu** ([Data/GameData.cs](DofusBankManager/Data/GameData.cs)) : les fichiers `items`, `crafts`, `skills` et `jobs` sont décodés par un mini-interpréteur AS2 ([Data/Avm1.cs](DofusBankManager/Data/Avm1.cs)).

Les fichiers sont stockés dans `%LOCALAPPDATA%\DofusBankManager` : cache des langues, banques sauvegardées et `packets.log`.

## Dépannage

Si la banque n'apparaît pas :

1. Cochez « Journaliser les paquets ».
2. Ouvrez la banque en jeu.
3. Lisez `%LOCALAPPDATA%\DofusBankManager\packets.log`. Il ne contient que les messages de banque et d'inventaire (règle 4).
