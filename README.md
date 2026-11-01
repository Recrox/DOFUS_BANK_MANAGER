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

1. Augmentez `<Version>` dans [DofusBankManager.csproj](DofusBankManager/DofusBankManager.csproj), par exemple `1.2.0`.
2. Committez et poussez sur `main`.

C'est tout : le workflow [release.yml](.github/workflows/release.yml) construit l'installateur sur GitHub, crée le tag `v1.2.0` et publie la Release avec le `.msi`. Suivez son avancement dans l'onglet **Actions** du dépôt (environ 3 minutes). Si la Release de cette version existe déjà, il ne fait rien. On peut aussi le relancer à la main : Actions › Release › **Run workflow**.

Changez au moins le 3e chiffre (`1.2.0` → `1.2.1`) : Windows Installer ignore le 4e.

#### Quel chiffre augmenter ? (`MAJEUR.MINEUR.CORRECTIF`)

| Changement | Chiffre | Exemple |
|---|---|---|
| **Incompatible** : données sauvegardées (`banks/`, `fm.json`, `hdv.json`…) plus lisibles par l'ancienne version, protocole du serveur de partage modifié (les anciennes versions ne peuvent plus synchroniser), fonctionnalité supprimée, nouveau prérequis à installer | **MAJEUR** | `1.4.2` → `2.0.0` |
| **Nouvelle fonctionnalité** compatible : nouvel onglet, filtre, colonne, bouton, option, nouvelle donnée affichée | **MINEUR** | `1.4.2` → `1.5.0` |
| **Correction** sans nouvelle fonctionnalité : bug, affichage, texte, performance, documentation livrée avec l'appli | **CORRECTIF** | `1.4.2` → `1.4.3` |

- Augmenter un chiffre **remet les suivants à 0** (`1.4.2` → `1.5.0`, jamais `1.5.2`).
- Plusieurs changements dans une même version : on prend **le plus important** (une fonctionnalité + un correctif = MINEUR).
- On ne change la version **qu'au moment de publier**, une seule fois par publication ; les commits intermédiaires gardent la version en cours.
- Un changement qui ne touche ni l'application ni l'installateur (ex. `server/`, README seul) ne demande pas de nouvelle version de l'application.

Les applications déjà installées compareront leur version au tag de la dernière Release et afficheront le bandeau de mise à jour. Ne changez jamais l'`UpgradeCode` de [installer/Package.wxs](installer/Package.wxs) : c'est lui qui permet au nouvel installateur de remplacer l'ancien.

## Lancement (développement)

Double-cliquez sur `Lancer DofusBankManager.bat`. L'application a besoin des droits administrateur pour lire le trafic réseau.

Ensuite, en jeu, **ouvrez votre banque** : elle est capturée, puis sauvegardée. Elle reste disponible au lancement suivant.

Pour recompiler (.NET 10 SDK requis) :

```
dotnet publish DofusBankManager\DofusBankManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
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
  - la lecture de la dernière Release GitHub du projet pour vérifier la version ([UpdateChecker.cs](DofusBankManager/UpdateChecker.cs)) ;
  - le serveur de partage des prix HDV du groupe ([HdvSync.cs](DofusBankManager/Hdv/HdvSync.cs), [server/](server/README.md)), **uniquement si l'utilisateur l'active** (désactivé par défaut). Il n'envoie que l'identifiant de l'objet, ses prix ×1/×10/×100, le prix moyen et la date du relevé.

  Ces requêtes ne contiennent aucune information sur le joueur. Toute nouvelle requête vers l'extérieur doit respecter la même règle.
- `packets.log` est un outil de diagnostic. Il **ne doit contenir que les messages de banque, d'inventaire et d'atelier** (`ECK`, `EL`, `EsK`, `EV`, `OAKO`, `OCO`, `OR`, `OQ`, `Ec`, `EmK`, `EMK`, `EH` pour l'hôtel de vente, et les seuls messages `Im` de forgemagie `117`/`183`/`194`), filtrés par `DofusTracker.IsLoggable`. Les autres messages `Im` (ex. connexion d'un ami) contiennent des noms : ils ne sont jamais journalisés. Ces messages ne décrivent que des objets et leurs caractéristiques. **Ne jamais élargir ce filtre** à des messages de connexion, de chat ou d'identité (`ASK` compris). Ne committez jamais ce fichier.
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
- **Données du jeu** ([Data/GameData.cs](DofusBankManager/Data/GameData.cs)) : les fichiers `items`, `crafts`, `skills`, `jobs`, `effects` et `monsters` sont décodés par un mini-interpréteur AS2 ([Data/Avm1.cs](DofusBankManager/Data/Avm1.cs)).
- **Forgemagie** ([Fm/FmTracker.cs](DofusBankManager/Fm/FmTracker.cs)) : voir ci-dessous.

Les fichiers sont stockés dans `%LOCALAPPDATA%\DofusBankManager` : cache des langues, banques sauvegardées, `settings.json` (thème), `fm.json` (historique FM), `fm_poids.json` (poids des stats), `hdv.json` (prix relevés), `pda.json` (prix des pierres d'âme par monstre), `marchands.json` (prix des cartes TTG chez les marchands) et `packets.log`.

### Onglet HDV (hôtel de vente)

Quand vous consultez un objet dans un hôtel de vente en jeu, le serveur envoie ses offres ; l'application les lit au passage et retient, pour chaque objet, le lot le moins cher en ×1, ×10 et ×100, le prix moyen affiché par l'HDV, et la date du relevé. Un historique des relevés est conservé par objet (50 au plus).

L'application **n'interroge jamais l'HDV elle-même** : cela demanderait d'envoyer des requêtes au serveur (règles 1 et 3). La base de prix se remplit donc au fil de vos consultations ; « Tous les objets du jeu » affiche aussi les objets sans prix. Les prix vieillissent : regardez la colonne « Relevé ».

**Partage entre amis** (bouton dans l'onglet HDV, désactivé par défaut) : les relevés de chaque membre du groupe sont envoyés à une base commune, et chacun reçoit ceux des autres en une quinzaine de secondes (bouton « Synchroniser » pour forcer). Un relevé plus récent remplace toujours un plus ancien ; les prix venus du groupe sont marqués « groupe » dans la colonne « Relevé ».

- **Rejoindre un groupe** (aucun compte nécessaire) : onglet HDV › **Partage entre amis** ; l'adresse du serveur est pré-remplie, collez le code de groupe reçu en privé, cochez **Activer le partage**, puis **Tester** et **Enregistrer**. Le réglage est conservé.
- **Héberger le serveur** (une seule personne par groupe) : voir [server/README.md](server/README.md).

Le **prix unitaire** est le meilleur des trois : ×1, ×10 ÷ 10, ×100 ÷ 100. L'onglet Banque s'en sert pour estimer la valeur de chaque objet et de la banque entière (objets sans prix relevé non comptés).

### Onglet Recettes : coût et sous-recettes

Le panneau de détail d'une recette affiche :

- les **caractéristiques** de l'objet fabriqué. Ce sont les valeurs **maximales** (champ `ef` du fichier de langue `items`), car le jeu ne fournit pas les minimums ;
- le **prix** de chaque ingrédient (prix unitaire HDV × quantité) ;
- le **coût total** de la recette, le montant à acheter pour les ingrédients manquants, et la marge si le prix HDV de l'objet est connu. Un ingrédient sans prix relevé est estimé par le coût de sa propre recette, quand celui-ci est connu ;
- les **sous-recettes** : un ingrédient fabricable (ex. Brochet farci pour le Kralano) se déplie avec la flèche ▸. Les quantités affichées sont celles qu'il faut pour toute la recette.

### Onglet Brisage

Cet onglet trouve les objets à briser pour générer des runes. Cochez une ou plusieurs caractéristiques et indiquez leur seuil (ex. Vitalité, minimum 104) : seuls les objets dans les bornes restent, les meilleurs en haut. La recherche peut porter sur :

- vos objets (banque et inventaire), avec leurs caractéristiques **réelles** ;
- les objets fabricables avec vos ressources, toutes les recettes ou tous les objets du jeu, avec leurs caractéristiques **maximales**.

Chaque ligne indique aussi le nombre d'exemplaires possédés, si l'objet est faisable, les ingrédients manquants, le coût de fabrication et le prix HDV. Le brisage reste à faire à la main, en jeu (règle 3).

### Onglet TTG (cartes)

L'onglet liste les cartes (commune, rare, épique, unique) et les paquets de cartes, dans l'ordre du classeur (No.). Il indique combien vous en possédez (banque et inventaire) et l'avancement de votre collection.

Les **prix marchands** se relèvent quand vous ouvrez la boutique d'un joueur en mode marchand : l'application lit la liste des objets en vente (`ECK4` puis `EL…`) et retient, pour chaque carte, le prix le plus bas de cette boutique. Le marchand n'est pas enregistré. Comme pour l'HDV, rien n'est demandé au serveur : seules les boutiques que vous consultez donnent un prix. Ces prix ne sont pas partagés avec le groupe.

Le panneau de détail montre l'évolution du prix chez les marchands et à l'HDV. L'onglet HDV affiche aussi un graphique de l'évolution du prix unitaire pour l'objet sélectionné.

### Onglet PDA (pierres d'âme)

L'HDV des pierres d'âme ne liste pas les offres par objet mais **par monstre** : chaque pierre pleine (de monstre, de gardien de donjon ou d'archi-monstre) est vendue sous les monstres qu'elle contient. L'application relève donc, pour chaque monstre consulté, la pierre la moins chère, le prix moyen, le nombre d'offres et la date. Ces prix ne vont ni dans l'onglet HDV ni dans le partage entre amis.

Une même liste mélange les catégories : celle d'un Bulbuisson contient des pierres de monstre, mais aussi des pierres d'archi-monstre où il accompagne l'archi (Bambouské le Camouflé). Les prix sont donc tenus **par monstre et par catégorie** (Monstre, Donjon, Archi-monstre), et une pierre d'archi-monstre ne compte que pour l'archi-monstre lui-même.

- **Prix relevés à l'HDV** : un monstre par ligne, avec sa race, son niveau et la catégorie de pierre (Monstre, Donjon, Archi-monstre).
- **Mes pierres** : vos pierres pleines (banque et inventaire) avec leur contenu. La valeur estimée d'une pierre est le prix relevé le plus haut parmi ses monstres.

Le panneau de détail montre l'évolution du prix du monstre sélectionné. Les noms des monstres viennent du fichier de langue `monsters`.

### Onglet FM (forgemagie)

L'onglet suit les objets que vous forgemagez et estime leur reliquat. Il ne fait que lire : aucune rune n'est passée à votre place (règle 3).

À chaque rune passée dans un atelier, l'application compare les caractéristiques de l'objet avant et après :

| Résultat | Comment il est reconnu | Reliquat estimé |
|---|---|---|
| Succès critique | objet recréé avec un gain, sans message d'échec | consommé du poids gagné s'il le couvre, sinon inchangé |
| Succès neutre | message `194` (« La magie n'a pas parfaitement fonctionné… ») | − variation nette de poids (jamais sous 0) |
| Échec | message `117` (« …ainsi que la diminution de la puissance de l'objet ») | + poids perdu |
| Échec sans effet | message `183` (« Malgré vos talents, la magie n'opère pas… ») | inchangé |

Le **résultat** vient du message affiché par le jeu (codes `INFOS_117`, `INFOS_183`, `INFOS_194` du fichier de langue) ; les **gains et pertes** viennent de la comparaison des stats. Retro recrée l'objet (nouvel identifiant) quand la rune modifie ses stats ; l'application suit l'objet posé dans l'atelier à travers ces versions. Le reliquat se calcule sur la variation **nette** de poids : un gain et une perte sur la même stat se compensent correctement. La rune consommée est rattachée au passage le plus proche.

Le poids d'une variation = nombre de points × poids par point de la stat. Les poids par défaut sont des valeurs communautaires ; ils sont modifiables dans `fm_poids.json` (bouton « Poids des stats », puis redémarrer l'application). Le reliquat reste une **estimation** : corrigez-le à la main si vous connaissez la vraie valeur. L'historique et le reliquat de chaque objet sont conservés entre deux lancements.

Pour que l'état « avant » de l'objet soit connu, lancez l'application **avant de vous connecter au jeu** (l'inventaire est alors capturé).

## Dépannage

Si la banque n'apparaît pas :

1. Cochez « Journaliser les paquets ».
2. Ouvrez la banque en jeu.
3. Lisez `%LOCALAPPDATA%\DofusBankManager\packets.log`. Il ne contient que les messages de banque et d'inventaire (règle 4).
