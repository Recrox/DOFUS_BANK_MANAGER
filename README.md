# Dofus Retro Manager

Lit votre banque Dofus Retro en écoutant passivement le trafic réseau, puis liste les recettes que vous pouvez fabriquer.

> **Avant de modifier le code, lisez les [règles de développement](#règles-de-développement).**
> Elles ne sont pas négociables : elles garantissent que l'application reste un simple outil de lecture.

## Installation (pour un utilisateur)

1. Téléchargez `DofusRetroManager-Setup.msi` depuis les [Releases](https://github.com/Recrox/DOFUS_RETRO_MANAGER/releases/latest), puis lancez-le.
2. Acceptez les conditions d'utilisation, puis choisissez le dossier d'installation.
3. À la fin, laissez cochée la case **« Lancer Dofus Retro Manager »** si vous voulez l'ouvrir tout de suite, puis acceptez la demande de droits administrateur.

Ensuite, l'application s'ouvre depuis le menu Démarrer (dossier « Dofus Retro Manager ») ou le Bureau.

Aucun prérequis : .NET est inclus. L'installateur ajoute la règle de pare-feu « Dofus Retro Manager ».

L'installateur n'est pas signé numériquement : Windows SmartScreen affichera « Windows a protégé votre ordinateur ». Cliquez sur **Informations complémentaires**, puis **Exécuter quand même**.

### Mises à jour

Au lancement, puis toutes les 30 secondes tant qu'elle reste ouverte, l'application vérifie si une version plus récente est publiée dans les Releases GitHub. Ces vérifications ne lisent que le numéro de la dernière version (une requête sans contenu sur la page « latest », hors de l'API GitHub, limitée à 60 requêtes par heure sans compte) ; l'API n'est interrogée que quand une nouvelle version apparaît. En cas d'échec, l'application patiente 15 minutes. « Plus tard » masque le bandeau jusqu'à la version suivante. Si c'est le cas, un bandeau s'affiche : **Mettre à jour** récupère l'installateur dans l'application (avec sa progression) et vérifie qu'il est intact (taille et empreinte SHA-256 publiées par GitHub). L'application se ferme alors, la nouvelle version s'installe sans assistant (une simple barre de progression de Windows, rien à accepter), dans le même dossier, puis l'application se rouvre d'elle-même et affiche ses nouveautés. Les banques sauvegardées, les prix et le thème sont conservés. Si l'installation échoue, l'ancienne version se rouvre et l'explique (journal de Windows Installer : `%TEMP%\DofusRetroManager\update.log`). Lancée hors installation (ex. dossier de développement), l'application ouvre l'assistant habituel à la place. Le bouton **Nouveautés** du bandeau ouvre la page de la nouvelle version sur GitHub.

**Ancien nom** : jusqu'à la 1.32, l'application s'appelait « Dofus Bank Manager ». Les versions installées se mettent à jour normalement (chaque Release joint aussi l'installateur sous son ancien nom, `DofusBankManager-Setup.msi`, que ces versions recherchent). Au premier lancement, le dossier `%LOCALAPPDATA%\DofusBankManager` est renommé en `DofusRetroManager` et l'ancienne règle de pare-feu « Dofus Bank Manager » est supprimée.

Le bouton **Nouveautés**, en haut de l'application (interface classique et HTML), affiche les changements de chaque version, la version installée mise en avant. Ils viennent de [CHANGELOG.md](CHANGELOG.md), intégré à l'application.

### Désinstallation

Deux possibilités :

- Menu Démarrer › Dofus Retro Manager › **Désinstaller Dofus Retro Manager** ;
- Paramètres Windows › Applications › Dofus Retro Manager › **Désinstaller**.

La désinstallation supprime l'application, les raccourcis et la règle de pare-feu. Vos données personnelles (banques sauvegardées, thème, cache des données du jeu) restent dans `%LOCALAPPDATA%\DofusRetroManager`, au cas où vous réinstalleriez. Supprimez ce dossier à la main pour tout effacer.

### Construire l'installateur

```
.\build-installer.ps1
```

Le fichier est créé dans `dist\DofusRetroManager-Setup.msi`. WiX Toolset est récupéré automatiquement via NuGet : rien à installer.

### Publier une nouvelle version

Chaque push sur `main` publie une Release, via le workflow [release.yml](.github/workflows/release.yml) :

- **Correctif** : rien à faire. Si `<Version>` dans [DofusRetroManager.csproj](DofusRetroManager/DofusRetroManager.csproj) n'est pas plus récente que la dernière Release, le dernier chiffre est augmenté automatiquement (`1.6.0` publiée → `1.6.1`).
- **Nouvelle fonctionnalité ou changement incompatible** : augmentez vous-même `<Version>` (`1.7.0`, `2.0.0`) avant de pousser ; c'est elle qui est publiée.

**Journal des changements** : avant de pousser, ajoutez vos changements à [CHANGELOG.md](CHANGELOG.md), dans la section `## <Version> — <date>` de la version du `.csproj` (une puce `- **Sujet** : ce qui change pour le joueur.` par changement). Ce texte est affiché par le bouton « Nouveautés » de l'application et devient les notes de la Release. Sans section pour la version publiée (correctif augmenté automatiquement), GitHub génère les notes à partir des commits.

Le workflow construit l'installateur, crée le tag (ex. `v1.6.1`), publie la Release avec le `.msi` et ses notes, puis **écrit la version publiée dans le `.csproj`** par un commit « Version 1.6.1 [skip ci] » (qui ne relance pas le workflow). **Faites donc `git pull` après chaque publication**, avant de committer à nouveau : sinon votre version locale reste l'ancienne et votre prochain push sera refusé. Suivez l'avancement dans l'onglet **Actions** du dépôt (environ 3 minutes). On peut aussi le relancer à la main : Actions › Release › **Run workflow**.

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

Double-cliquez sur `Lancer DofusRetroManager.bat`. L'application a besoin des droits administrateur pour lire le trafic réseau.

Ensuite, en jeu, **ouvrez votre banque** : elle est capturée, puis sauvegardée. Elle reste disponible au lancement suivant.

Pour recompiler (.NET 10 SDK requis) :

```
dotnet publish DofusRetroManager\DofusRetroManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
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
- Gestion des fenêtres (onglet Switcheur) : l'application peut **mettre au premier plan** une fenêtre du jeu, comme Alt+Tab : quand le joueur le demande (raccourci, clic sur une bulle ou un bouton), ou, si l'option est activée, quand un de ses personnages reçoit une invitation de groupe ou une demande d'échange (c'est toujours le joueur qui accepte), ou quand commence le tour de jeu d'un de ses personnages en combat (c'est toujours le joueur qui joue). Elle ne lit que le titre des fenêtres et n'envoie **jamais** de touche, de clic ni de message à une fenêtre du jeu. Pas de « suivre le chef », pas de diffusion d'une touche à plusieurs fenêtres.
- Une fonctionnalité qui ferait jouer à la place du joueur (crafter, déposer, vendre automatiquement) est **refusée**, même « juste pour tester ».

C'est ce qui sépare un outil d'aide d'un bot. Les sanctions d'Ankama visent surtout cette catégorie.

### 4. Protéger les données du joueur

Le trafic capturé contient des informations sensibles : nom de compte, ticket de connexion, messages privés.

- **Ne lire que les messages utiles** à la banque et à l'inventaire (voir [DofusTracker.cs](DofusRetroManager/Net/DofusTracker.cs)). Les autres sont ignorés.
- Exception, pour les onglets Combat et TTG : sont aussi lus la map (`GDM`, `GM`), les caractéristiques du personnage (`As`), les fins de combat (`GE`), le groupe (`PM`, `PV`), les sorts du personnage (`SL`), l'entrée en combat (`GJK`) et les sorts lancés et attaques à l'arme (`GA` de type 300, 302 et 303, y compris ceux envoyés par le client du joueur, pour savoir quelle fenêtre joue). Ces messages contiennent aussi des noms de joueurs. On n'en garde que :
  - les groupes de monstres de la map (modèles, niveaux, étoiles) ;
  - les joueurs en mode marchand (pseudo et case) : pour une boutique consultée, ils sont enregistrés avec ses prix de cartes TTG, sur ce PC seulement, **jamais partagés** ;
  - aucun autre sprite (joueurs, PNJ…) ;
  - la sagesse et la prospection du personnage ;
  - la ligne du personnage en fin de combat (XP, kamas, objets) et les monstres affrontés ;
  - le niveau et la prospection des membres du groupe, jamais leur nom ;
  - les sorts du personnage et ceux qu'il lance en combat (arme comprise) et son identifiant, jamais ceux des autres combattants ;
  - l'initiative du personnage (As, onglet Switcheur : ordre de jeu) ;
  - la classe et le sexe de vos personnages (`ASK`, déjà lu pour l'inventaire ; onglet Switcheur : icône), gardés dans `chasse.json` ;
  - les points de vie du personnage (As, et en combat `GTM` : seule la ligne du personnage, celles des autres combattants sont ignorées) ;
  - l'identifiant des challenges du combat et leur résultat (`Gd`, onglet Arène), rien d'autre.
- Pour les notifications de l'onglet Switcheur sont aussi lus la demande d'échange (ERK : seul l'identifiant du destinataire, pour savoir si elle vise le personnage de la fenêtre) et l'invitation de groupe (PIK : seul le nom de l'invité, comparé à celui du personnage ; celui de l'invitant n'est pas lu). Rien n'en est gardé, journalisé ni affiché d'autre que « Demande d'échange » ou « Invitation de groupe ».
- Pour la récolte (onglet Carte, superposition) sont aussi lus la version et la clé de lecture de la map (`GDM`, déjà lu pour la map), qui servent à lire ses ressources dans le fichier de la map du client installé, et l'état des objets interactifs de la map (`GDF` : numéro de case et état, rien d'autre). On n'en garde, dans `recolte.json`, que les ressources de chaque map visitée (case et type), la clé de la map et les temps de repousse observés. Ce sont des données du jeu, pas du joueur : elles restent sur ce PC et ne sont **jamais partagées** (règle 5).

  Ces messages ne sont **jamais journalisés**, ni dans `packets.log` ni dans le journal de l'application, et rien n'en sort de la machine.
- **Aucune donnée du joueur ne sort de la machine** : pas de télémétrie, pas d'envoi de la banque ni de l'inventaire. Les seules requêtes réseau autorisées sont :
  - le téléchargement des données du jeu depuis le CDN d'Ankama ([GameData.cs](DofusRetroManager/Data/GameData.cs)) ;
  - la lecture de la dernière Release GitHub du projet pour vérifier la version, et le téléchargement de son installateur quand l'utilisateur le demande ([UpdateChecker.cs](DofusRetroManager/UpdateChecker.cs)) ;
  - le serveur de partage des prix du groupe ([HdvSync.cs](DofusRetroManager/Hdv/HdvSync.cs), [server/](server/README.md)), **uniquement si l'utilisateur l'active** (désactivé par défaut). Il n'envoie que des prix : pour l'HDV, l'identifiant de l'objet, ses prix ×1/×10/×100, le prix moyen et la date du relevé ; pour les cartes TTG, l'identifiant de la carte, son prix, la quantité en vente et la date (jamais le marchand) ; pour les pierres d'âme, l'identifiant du monstre, la catégorie de pierre, le prix, le nombre d'offres, le prix moyen et la date.

  Ces requêtes ne contiennent aucune information sur le joueur. Toute nouvelle requête vers l'extérieur doit respecter la même règle.
- `packets.log` est un outil de diagnostic. Il **ne doit contenir que les messages de banque, d'inventaire et d'atelier** (`ECK`, `EL`, `EsK`, `EV`, `OAKO`, `OCO`, `OR`, `OQ`, `Ec`, `EmK`, `EMK`, `EH` pour l'hôtel de vente, et les seuls messages `Im` de forgemagie `117`/`183`/`194`), filtrés par `DofusTracker.IsLoggable`. Les autres messages `Im` (ex. connexion d'un ami) contiennent des noms : ils ne sont jamais journalisés. Ces messages ne décrivent que des objets et leurs caractéristiques. **Ne jamais élargir ce filtre** à des messages de connexion, de chat ou d'identité (`ASK` compris). Ne committez jamais ce fichier.
- La journalisation des paquets reste **désactivée par défaut**, et elle n'écrit pas les adresses IP.

### 5. Données du jeu

- Les fichiers de langue (objets, recettes, métiers) appartiennent à Ankama. Ils sont **téléchargés depuis leur CDN et mis en cache localement**. **Ne pas les committer ni les redistribuer.**
- Données tierces : seules celles que le mainteneur est autorisé à utiliser peuvent être livrées avec l'application, comme l'XP des monstres ([Data/monstres-xp.json](DofusRetroManager/Data/monstres-xp.json), réduite à l'identifiant, au niveau et à l'XP). Ne jamais récupérer de données en masse sur un site qui l'interdit (robots.txt, conditions d'utilisation).
- Rester sobre : un fichier n'est téléchargé que si sa version a changé (voir [GameData.cs](DofusRetroManager/Data/GameData.cs)). Pas de requêtes en boucle.

### 6. Rappel

Même passif, cet outil est un logiciel tiers. Les conditions d'utilisation d'Ankama peuvent l'interdire, et aucune technique ne garantit l'absence de détection. **Chacun l'utilise à ses propres risques.** Respecter les règles ci-dessus réduit le risque, sans l'annuler.

**En cas de doute sur une fonctionnalité, la réponse par défaut est non.**

## Fonctionnement

- **Capture** ([Net/PacketSniffer.cs](DofusRetroManager/Net/PacketSniffer.cs)) : socket brute Windows (`SIO_RCVALL`), sans Npcap. Les flux TCP sont réassemblés, puis découpés en messages sur `\0`.
- **Pare-feu** ([Net/FirewallRule.cs](DofusRetroManager/Net/FirewallRule.cs)) : sans autorisation, Windows bloque le trafic entrant vers une socket brute, et seuls les messages envoyés par le client sont visibles. Au démarrage de la capture, l'appli ajoute donc la règle entrante « Dofus Retro Manager », limitée à son exécutable. Pour la retirer : `netsh advfirewall firewall delete rule name="Dofus Retro Manager"`.
- **Protocole** ([Net/DofusTracker.cs](DofusRetroManager/Net/DofusTracker.cs)) : `ECK5` ouvre la banque, `EL…` donne son contenu, `EsK…` ses mouvements. `ASK` / `OAKO` / `OR` / `OQ` suivent l'inventaire.
- **Données du jeu** ([Data/GameData.cs](DofusRetroManager/Data/GameData.cs)) : les fichiers `items`, `crafts`, `skills`, `jobs`, `effects`, `monsters`, `monsterdrops` (taux de drop officiels), `maps` (coordonnées et zones), `hints` (points d'intérêt de la carte), `dungeons` (noms des donjons), `houses` (maisons), `interactiveobjects` (ressources récoltables) et `fightChallenge` (noms des challenges) sont décodés par un mini-interpréteur AS2 ([Data/Avm1.cs](DofusRetroManager/Data/Avm1.cs)).
- **Forgemagie** ([Fm/FmTracker.cs](DofusRetroManager/Fm/FmTracker.cs)) : voir ci-dessous.
- **Interface** : deux styles (Classique, Moderne) et trois thèmes ([Themes/](DofusRetroManager/Themes)), avec la police [Plus Jakarta Sans](https://github.com/tokotype/PlusJakartaSans), intégrée à l'application sous licence SIL Open Font License ([Fonts/OFL.txt](DofusRetroManager/Fonts/OFL.txt)).
- **Interface HTML** (bouton « HTML » en haut, retour par « Interface classique ») : une page HTML/CSS ([Web/](DofusRetroManager/Web)) affichée par Microsoft Edge WebView2 (fourni avec Windows 11). Elle ne recalcule rien : elle affiche l'état de l'interface classique et lui renvoie les actions ([MainWindow.WebUi.cs](DofusRetroManager/MainWindow.WebUi.cs)), donc les chiffres sont les mêmes. Tous les onglets y sont ; seuls les réglages du partage entre amis (onglet HDV) restent dans l'interface classique. La carte y est dessinée par la page avec les tuiles et icônes que l'application rend depuis le client installé. Le style (Classique, Moderne) et le thème (Jour, Crépuscule, Nuit) se changent en haut de la page, comme dans l'interface classique. La page est servie depuis l'exe (`https://app.local/`) : elle ne charge ni n'envoie rien vers l'extérieur (politique de sécurité de la page, navigation bloquée hors de l'application). Au premier lancement, l'application s'ouvre en interface HTML, style Moderne (sans WebView2, elle revient à l'interface classique) ; ensuite, le choix est retenu dans `settings.json`.

Les fichiers sont stockés dans `%LOCALAPPDATA%\DofusRetroManager` : cache des langues, banques sauvegardées, `settings.json` (style Classique ou Moderne, choisi par le bouton en haut, et thème Jour, Crépuscule ou Nuit), `fm.json` (historique FM), `fm_poids.json` (poids des stats), `hdv.json` (prix relevés), `pda.json` (prix des pierres d'âme par monstre), `marchands.json` (prix des cartes TTG chez les marchands), `chasse.json` (archi-monstres aperçus et combats), `organizer.json` (onglet Switcheur : raccourcis, notifications, personnages exclus), `recolte.json` (ressources des maps visitées et temps de repousse observés), `monstres.json` (XP des monstres, facultatif : complète celle livrée avec l'application, voir l'onglet Combat) et `packets.log`.

### Onglet HDV (hôtel de vente)

Quand vous consultez un objet dans un hôtel de vente en jeu, le serveur envoie ses offres ; l'application les lit au passage et retient, pour chaque objet, le lot le moins cher en ×1, ×10 et ×100, le prix moyen affiché par l'HDV, et la date du relevé. Un historique des relevés est conservé par objet (50 au plus).

L'application **n'interroge jamais l'HDV elle-même** : cela demanderait d'envoyer des requêtes au serveur (règles 1 et 3). La base de prix se remplit donc au fil de vos consultations ; « Tous les objets du jeu » affiche aussi les objets sans prix. Les prix vieillissent : regardez la colonne « Relevé ».

**Partage entre amis** (bouton dans l'onglet HDV, désactivé par défaut) : les relevés de chaque membre du groupe (HDV, mais aussi prix marchands TTG et pierres d'âme) sont envoyés à une base commune, et chacun reçoit ceux des autres en une quinzaine de secondes (bouton « Synchroniser » pour forcer). Un relevé plus récent remplace toujours un plus ancien ; les prix venus du groupe sont marqués « groupe » dans la colonne « Relevé ».

- **Rejoindre un groupe** (aucun compte nécessaire) : onglet HDV › **Partage entre amis** ; l'adresse du serveur est pré-remplie, collez le code de groupe reçu en privé, cochez **Activer le partage**, puis **Tester** et **Enregistrer**. Le réglage est conservé.
- **Héberger le serveur** (une seule personne par groupe) : voir [server/README.md](server/README.md).

Le **prix unitaire** est le meilleur des trois : ×1, ×10 ÷ 10, ×100 ÷ 100. L'onglet Banque affiche ce prix unitaire et le prix moyen de l'HDV, et s'en sert pour estimer la valeur de chaque objet et de la banque entière (objets sans prix relevé non comptés).

### Onglet Recettes : coût et sous-recettes

Le panneau de détail d'une recette affiche :

- les **caractéristiques** de l'objet fabriqué. Ce sont les valeurs **maximales** (champ `ef` du fichier de langue `items`), car le jeu ne fournit pas les minimums ;
- le **prix** de chaque ingrédient (prix unitaire HDV × quantité) ;
- le **coût total** de la recette, le montant à acheter pour les ingrédients manquants, et la marge si le prix HDV de l'objet est connu. Un ingrédient sans prix relevé est estimé par le coût de sa propre recette, quand celui-ci est connu ;
- deux **bénéfices** : en comptant tous les ingrédients au prix HDV, et **hors ressources possédées** (seul ce qu'il faut acheter est compté). Le champ **Crafts** (par défaut, et avec le bouton **Max** : autant que vos ressources le permettent, au moins 1) multiplie les quantités, les coûts et la vente ;
- les **sous-recettes** : un ingrédient fabricable (ex. Brochet farci pour le Kralano) se déplie avec la flèche ▸. Les quantités affichées sont celles qu'il faut pour toute la recette.

**Rentabilité** : la liste des recettes affiche pour chacune le **coût** (prix HDV des ingrédients, ou coût de leur sous-recette), le prix de **vente** de l'objet à l'HDV, le **bénéfice** (vert s'il est positif, rouge à perte) et la **marge** en pourcentage du coût. Toutes ces colonnes se trient d'un clic. Dans « Afficher », **Rentables** ne garde que les recettes à bénéfice positif, et **Coût et vente connus** toutes celles dont le bénéfice est calculable ; les deux sont triées du plus gros bénéfice au plus petit. **Meilleure marge** garde les recettes rentables, triées par marge. Dans ces trois modes, le champ **Vente min.** (10 000 kamas par défaut, 0 pour aucun minimum) écarte les objets qui se vendent moins cher. Le coût reste vide tant qu'un ingrédient n'a pas de prix : plus le groupe relève de prix, plus la liste se remplit.

### Onglet Brisage

Cet onglet trouve les objets à briser pour générer des runes. Cochez une ou plusieurs caractéristiques et indiquez leur seuil (ex. Vitalité, minimum 104) : seuls les objets dans les bornes restent, les meilleurs en haut. La recherche peut porter sur :

- vos objets (banque et inventaire), avec leurs caractéristiques **réelles** ;
- les objets fabricables avec vos ressources, toutes les recettes ou tous les objets du jeu, avec leurs caractéristiques **maximales**.

Chaque ligne indique aussi le nombre d'exemplaires possédés, si l'objet est faisable, les ingrédients manquants, le coût de fabrication et le prix HDV.

**Runes estimées** : chaque objet affiche les runes qu'il devrait donner, leur valeur au prix unitaire HDV et le **gain** du brisage (valeur des runes moins le prix HDV de l'objet pour vos objets, moins son coût de fabrication pour les recettes). Le panneau de droite détaille chaque rune : quantité, prix par unité, total. Le champ **Quantité à briser** (par défaut, et avec **Max** : tous les exemplaires possédés) multiplie les runes, leur valeur et le gain.

Aucune formule officielle n'existe pour Retro : c'est une **estimation**. Pour chaque caractéristique, poids de runes = jet × poids par point × niveau de l'objet × 36 % / 100, réparti de la plus grosse rune (Ra) à la plus petite (quantité décimale pour les runes simples). Exemple : la Coiffe du Kitsou (niveau 61, 150 de vitalité) donne 1 Ra Vi. Le brisage reste à faire à la main, en jeu (règle 3).

### Onglet TTG (cartes)

L'onglet liste les cartes (commune, rare, épique, unique) et les paquets de cartes, dans l'ordre du classeur (No.). Il indique combien vous en possédez (banque et inventaire) et l'avancement de votre collection. Comme dans l'onglet HDV, seules les cartes avec un prix relevé s'affichent ; cochez « Toutes les cartes du jeu » pour voir toute la collection.

Les **prix marchands** se relèvent quand vous ouvrez la boutique d'un joueur en mode marchand : l'application lit la liste des objets en vente (`ECK4` puis `EL…`) et retient, pour chaque carte, le prix le plus bas de cette boutique, avec le **pseudo du marchand**, sa **map** (coordonnées et zone) et sa **case**, lus sur la map. Ces trois informations restent sur votre PC et ne sont **jamais partagées**. La liste affiche, pour chaque carte, le marchand et la position de la meilleure offre de la période (colonnes « Chez » et « Position »). Un clic sur une position ouvre l'onglet Carte centré sur sa case, avec un repère au nom du marchand. Une même carte chez un même marchand n'est relevée qu'une fois par heure. Comme pour l'HDV, rien n'est demandé au serveur : seules les boutiques que vous consultez donnent un prix. Si le partage entre amis est activé (onglet HDV), ces prix sont partagés avec le groupe, sans le marchand ; les relevés venus du groupe sont marqués « groupe ».

Le panneau de détail liste, au choix, les **offres vues sur la période** ou **tout l'historique** de la carte, chaque relevé avec son marchand et sa position. Par défaut, les **offres vues sur la période** (7 jours par défaut, champ « Période »), de la moins chère à la plus chère, avec le marchand, sa position et la date ; puis l'évolution du prix chez les marchands et à l'HDV. Les relevés d'avant cette version, et ceux venus du groupe, n'ont pas de marchand. L'onglet HDV affiche aussi un graphique de l'évolution du prix unitaire pour l'objet sélectionné.

### Onglet PDA (pierres d'âme)

L'HDV des pierres d'âme ne liste pas les offres par objet mais **par monstre** : chaque pierre pleine (de monstre, de gardien de donjon ou d'archi-monstre) est vendue sous les monstres qu'elle contient. L'application relève donc, pour chaque monstre consulté, la pierre la moins chère, le prix moyen, le nombre d'offres et la date. Ces prix ne vont pas dans l'onglet HDV ; ils sont partagés avec le groupe si le partage entre amis est activé.

Une même liste mélange les catégories : celle d'un Bulbuisson contient des pierres de monstre, mais aussi des pierres d'archi-monstre où il accompagne l'archi (Bambouské le Camouflé). Les prix sont donc tenus **par monstre et par catégorie** (Monstre, Donjon, Archi-monstre), et une pierre d'archi-monstre ne compte que pour l'archi-monstre lui-même.

- **Prix relevés à l'HDV** : un monstre par ligne, avec sa race, son niveau et la catégorie de pierre (Monstre, Donjon, Archi-monstre). « Tous les monstres du jeu » ajoute ceux dont le prix n'a pas encore été relevé.
- **Mes pierres** : vos pierres pleines (banque et inventaire) avec leur contenu. La liste **Personnage** choisit de qui les afficher, ou « Tous les personnages » : celui en jeu, et les autres d'après leur dernière banque connue (`banks/`). La colonne « Où » indique le personnage (« Banque de … », « Inventaire de … ») ; une banque commune à plusieurs personnages d'un même compte (mêmes objets) n'est comptée qu'une fois. L'onglet Banque a la même liste **Personnage** : la banque du personnage en jeu, ou la dernière connue d'un autre, avec sa date. La valeur estimée d'une pierre est le prix relevé le plus haut parmi ses monstres.

Le panneau de détail montre l'évolution du prix du monstre sélectionné. Les noms des monstres viennent du fichier de langue `monsters`.

### Onglet Combat

L'onglet affiche les **groupes de monstres de la map** où vous êtes (monstres, niveau du groupe, étoiles), votre **niveau**, votre **sagesse**, votre **prospection**, et le niveau et la prospection du **groupe** (somme des membres, lue dans les messages de groupe ; les vôtres seuls hors groupe).

- **Multi-compte** : chaque fenêtre de jeu est suivie séparément. La liste « Personnage » choisit celle à afficher ; « Auto » suit la fenêtre qui vient de changer de map ou d'être mise au premier plan, et affiche son personnage. En combat, au début du tour d'un de vos personnages, ou dès qu'il lance un sort ou attaque, l'onglet passe sur lui (même s'il a été choisi à la main). Si l'application a démarré après la connexion, son premier sort ou sa première attaque suffit à le reconnaître. La rentabilité est celle du personnage affiché, avec le total de tous les personnages en dessous. Une fenêtre fermée ou déconnectée disparaît de la liste ; après une reconnexion, le personnage reprend sa place.
- **Lancez l'application avant de choisir votre personnage** : c'est à ce moment qu'elle apprend son identifiant, nécessaire pour retrouver sa ligne dans les résultats de combat. Sinon, seuls les combats en solo sont comptés (revenir à la sélection des personnages suffit), et le nom du personnage n'apparaît qu'après son premier combat en solo. De même, la sagesse et la prospection ne sont envoyées par le jeu qu'à la connexion ou quand elles changent : changer une pièce d'équipement suffit à les faire apparaître.

- **Archi-monstres** : quand un groupe contenant un archi-monstre apparaît sur la map, une alerte sonore retentit (case « Alerte sonore archi-monstre ») : un arpège cristallin généré par l'application, dont le volume se règle avec le curseur à côté (le son est rejoué au relâchement). L'historique liste les derniers archi-monstres vus, avec leur niveau, leur **position** (coordonnées et zone de la map ; un clic l'ouvre sur la carte, comme la map actuelle affichée en haut de l'onglet) et le dernier prix relevé de leur pierre (onglet PDA). Un même archi sur la même map n'est signalé qu'une fois toutes les 10 minutes.
- **Drops (taux du jeu)** : les taux de drop officiels viennent du fichier de langue `monsterdrops` d'Ankama (CDN, mis en cache comme les autres). Pour chaque monstre du groupe, on prend le taux de son grade (retrouvé par son niveau ; un boss affiché sans niveau prend son grade 3), puis chance = min(100 %, taux × prospection du groupe / 100 × (1 + étoiles / 100)). Un paquet de cartes a une chance fixe (1 sur 100 à 1 sur 2 000 selon le monstre). Un objet est bloqué (🔒) si la prospection du groupe est sous son minimum. La flèche ▸ de la colonne déplie, sous le groupe, le tableau de tous ses drops : chance (la meilleure si plusieurs monstres le donnent), nombre de monstres qui le donnent, prospection minimale, maximum par combat, prix unitaire, valeur par combat et état. Les objets bloqués et les drops sous condition (trophées, jetons, événements, objets de quête) y sont grisés, sans être comptés. **Drops estimés** = Σ, pour chaque objet débloqué, min(maximum par combat, somme des chances) × prix unitaire (HDV, sinon prix marchand TTG de la période, sinon le dernier relevé). Ce sont des espérances par combat, pas une garantie.
- **XP et drops observés** : les données du jeu ne contiennent pas l'XP des monstres. L'application est livrée avec l'XP de base de 1 063 monstres ([Data/monstres-xp.json](DofusRetroManager/Data/monstres-xp.json)) : celle du grade 1, proportionnelle au niveau pour les autres grades (estimation). Un fichier `monstres.json` au même format (`{ "monsters": [ { "id", "previewGrade": { "level", "xp" } } ] }`) placé dans `%LOCALAPPDATA%\DofusRetroManager` la complète ou la remplace. Pour un monstre absent de ces données, l'application les **apprend de vos fins de combat** : l'XP gagnée est ramenée sans sagesse, puis répartie entre les monstres selon leur niveau ; les objets obtenus sont attribués aux monstres du combat, d'où une fréquence observée. Pour un groupe de la map, l'XP estimée applique votre sagesse actuelle et les étoiles du groupe. Un monstre encore jamais combattu prend l'XP par niveau moyenne des monstres déjà appris (valeur précédée de « ≈ ») ; avant tout combat, les colonnes affichent « à apprendre ». Ce sont des **estimations** : elles se précisent au fil des combats, et ne tiennent compte ni de la taille du groupe ni de l'écart de niveau.
- **Rentabilité** : la session additionne l'XP, les kamas et les drops (au prix unitaire HDV) de vos combats, puis projette ce rythme sur 30, 60 et 120 minutes. « Nouvelle session » repart de zéro ; après plus d'une heure sans combat, une nouvelle session s'ouvre d'elle-même.

- **Économe** (challenge : chaque sort une seule fois par combat) : l'onglet affiche les sorts du personnage, et son arme (corps-à-corps, qui compte comme un sort), avec leurs icônes du jeu (fond, cadre et symbole lus dans le client installé, couleurs du fichier de langue des sorts) : normales tant qu'ils sont disponibles, grisées une fois lancés (comme un sort qu'on ne peut pas lancer), grisées et encadrées de rouge s'ils sont lancés deux fois (challenge raté). La liste repart de zéro à chaque entrée en combat (ou avec « Remettre à zéro »). Un échec critique compte comme un sort lancé. Les sorts du personnage sont envoyés par le jeu à la connexion, puis gardés d'une fois sur l'autre.

Comme le reste, l'onglet ne fait qu'afficher : c'est toujours le joueur qui se déplace, qui choisit ses sorts et qui combat (règle 3).

### Onglet Switcheur (multi-compte)

L'onglet liste les fenêtres Dofus Retro ouvertes (le personnage est lu dans le titre de la fenêtre) avec la **tête du personnage** (sa classe et son sexe, comme dans le jeu), leur niveau, leur **initiative** et leur état (en combat ou non). Elles sont classées dans l'**ordre de jeu** : initiative décroissante, initiative inconnue en dernier. L'initiative est envoyée par le jeu à la connexion du personnage ou quand elle change (changer une pièce d'équipement suffit) ; en combat, elle varie avec les points de vie : elle n'est donc retenue que hors combat. La tête est lue, en lecture seule, dans les fichiers du client installé (`clips/artworks/mini`), aux couleurs par défaut de la classe : les couleurs choisies et l'équipement du personnage ne sont pas représentés. La classe et le sexe sont envoyés par le jeu au choix du personnage, puis retenus : lancez l'application avant de choisir vos personnages la première fois.

- **Switcheur activé** (case en haut) : décochée, plus aucun raccourci, notification ni bascule de fenêtre ; la liste reste affichée.
- **Figer l'ordre à l'entrée en groupe** (activé par défaut) : quand un de vos personnages rejoint un groupe, l'ordre de jeu actuel est enregistré (les personnages qui n'y étaient pas sont ajoutés à la suite) et ne bouge plus.
- **Anti-rebond** (160 ms par défaut) : une nouvelle pression de raccourci plus rapprochée que ce délai est ignorée, pour qu'un double appui ne saute pas un personnage.

- **Raccourcis** « Personnage suivant » et « Précédent » (par défaut Ctrl+Alt+→ et Ctrl+Alt+←, modifiables : cliquez dans le champ puis appuyez sur la combinaison) : ils marchent même quand le jeu a le focus et mettent au premier plan la fenêtre suivante dans l'ordre de jeu. Le bouton **Afficher** fait de même pour une fenêtre précise.
- **Ordre personnalisé** : glissez-déposez les lignes pour placer les personnages dans l'ordre voulu. Le nouvel ordre s'applique tout de suite (raccourcis, superposition) ; **Enregistrer l'ordre** le garde d'un lancement à l'autre, **Ordre par initiative** revient au tri automatique. Un personnage pas encore placé (nouveau compte) se range après les autres, par initiative.
- **Personnage principal** : cliquez sur l'étoile ☆ d'un personnage pour en faire le principal (un seul ; un nouveau clic l'enlève). Le raccourci « Principal ★ » (par défaut Ctrl+Alt+↑, modifiable) affiche directement sa fenêtre. Il est aussi marqué d'une étoile dans la superposition.
- **Exclure** sort un personnage de la rotation (ex. un compte métier) ; le choix est retenu.
- **Notifications** (chacune activable) : début de combat, début de votre tour, demande d'échange, invitation de groupe. Elles prennent la forme d'une **bulle** en bas à droite (elle ne prend pas le focus ; un clic affiche la fenêtre concernée), d'un **clignotement** dans la barre des tâches et d'un **son** (même volume que l'alerte archi-monstre). Rien n'est notifié pour la fenêtre déjà au premier plan. **Basculer sur la fenêtre invitée** (activé par défaut) : à une invitation de groupe ou une demande d'échange, la fenêtre du personnage concerné passe au premier plan ; vous n'avez plus qu'à accepter. **Basculer au début du tour** (activé par défaut) : en combat, la fenêtre du personnage dont c'est le tour passe au premier plan (personnages exclus mis à part).

- **Superposition en jeu** (case à cocher, désactivée par défaut) : une petite fenêtre au-dessus du jeu affiche le personnage de la fenêtre au premier plan (elle change dès que vous basculez de fenêtre : niveau du personnage et du groupe, prospection du personnage et du groupe, sagesse), l'**Économe**, les **groupes de la map** (les 3 plus forts, avec XP et drops estimés, archi-monstre encadré de rouge) et l'**ordre de jeu** (avec l'initiative et la barre de vie de chaque personnage, en pourcentage). Chaque bloc s'active séparément, l'opacité se règle. Elle n'apparaît que quand une fenêtre de jeu est au premier plan et laisse passer les clics vers le jeu ; **Déplacer** permet de la faire glisser à sa place, puis **Verrouiller**. C'est une fenêtre de l'application posée par-dessus : rien n'est injecté ni dessiné dans le client (règle 2).

Comme le reste, l'onglet ne joue pas à votre place : il ne fait qu'afficher une fenêtre quand vous le demandez (règle 3).

### Onglet Arène

- **Chronomètre** : **Démarrer**, **Pause** / **Reprendre**, **Remettre à zéro**. Pendant qu'il tourne, chaque fin de combat est comptée (un combat à plusieurs de vos personnages compte une fois) et le bilan se met à jour en temps réel : durée, nombre de combats, rythme (ex. « 59 combats · moyenne de 1 combat / 50 s »), gain moyen par combat et total (kamas + drops au prix HDV, sinon prix marchand), gain et XP par heure. La liste des combats comptés est en dessous.
- **Challenges Économe et Barbare** : le jeu annonce les challenges du combat (`Gd`) et leur résultat. En attendant, l'état est suivi d'après les actions de vos personnages : **Économe** (chaque sort et l'arme une seule fois par combat) est raté dès qu'une action est répétée, **Barbare** (aucun sort, seulement l'arme) dès qu'un sort est lancé. Les autres challenges annoncés sont listés par leur nom.

Comme le reste, l'onglet ne fait qu'afficher : c'est toujours le joueur qui choisit ses actions (règle 3).

### Onglet Carte

L'onglet affiche la carte du monde (continent amaknien ou Incarnam) avec ses points d'intérêt : zaaps, temples et statues de classes, hôtels de vente, ateliers, donjons (nom et niveau), territoires de conquête et lieux divers (banques, tavernes, transports…). Glissez pour déplacer la carte, molette pour zoomer ; la case survolée affiche ses coordonnées, sa zone et ce qu'elle contient.

- **Maisons** : la porte de chaque maison (fichier de langue `houses`), avec son nom.
- **Ressources** : choisissez un métier dans « Ressources » (Bûcheron, Pêcheur…), puis cochez les ressources voulues (icône de l'objet récolté, ex. If pour le Bois d'If) : elles s'affichent sur les maps où elles poussent, avec leur nombre (« If ×3 »). Les maps du client sont chiffrées et leur clé n'arrive qu'en entrant sur la map : seules les **maps déjà visitées avec l'application lancée** sont connues (leur nombre est affiché à côté). La carte se complète donc au fil de vos déplacements ; rien n'est repris d'un site tiers (règle 5).
- **Récolte sur la map** (panneau de droite, et bloc « Récolte » de la superposition en jeu, onglet Switcheur) : les ressources de la map du personnage affiché, disponibles et récoltées (« Petits poissons (mer) ×4 », « Poissons (mer) ×1 / 2 · 1 récoltée(s), réapparition dans ~2 min »). Le jeu ne donne pas le temps de repousse : l'application l'apprend en voyant une ressource récoltée puis réapparaître pendant que vous êtes sur la map ; avant cela, elle affiche depuis quand elle est récoltée. C'est une **estimation**. C'est toujours le joueur qui récolte (règle 3).
- **Enclos, parchemins et prix des maisons** : pas disponibles pour l'instant.
- **Filtres** : un bouton par catégorie (Zaaps et Donjons cochés par défaut). La **recherche** porte sur toutes les catégories : nom (« Zaap », « Temple Iop », « Bouftou »), zone ou coordonnées (`[4,-18]`). Un clic dans la liste centre la carte sur le point et l'encadre.
- **Vos personnages** : la map actuelle de chaque fenêtre de jeu (celle lue pour l'onglet Combat) est marquée sur la carte ; **Ma position** centre la vue sur le personnage affiché dans l'onglet Combat.

La carte et les icônes ne sont **pas livrées** avec l'application : elles sont lues, en lecture seule, dans les fichiers du client installé (`clips/maps/0.swf`, `3.swf` et `hints.swf`), comme les icônes de sorts (règles 2 et 5). Les points d'intérêt viennent des fichiers de langue `hints` et `dungeons` du CDN ; les ressources, des fichiers de langue `interactiveobjects` et `skills` et des maps du client (`data/maps`). Sans Dofus Retro installé, la carte reste vide.

### Onglet FM (forgemagie)

L'onglet suit les objets que vous forgemagez et estime leur reliquat. Il ne fait que lire : aucune rune n'est passée à votre place (règle 3).

À chaque rune passée dans un atelier, l'application compare les caractéristiques de l'objet avant et après :

| Résultat | Comment il est reconnu | Reliquat estimé |
|---|---|---|
| Succès critique | objet recréé avec un gain, sans message d'échec | consommé du poids gagné s'il le couvre, sinon inchangé |
| Succès neutre | message `194` (« La magie n'a pas parfaitement fonctionné… ») | − variation nette de poids (jamais sous 0) |
| Échec | message `117` (« …ainsi que la diminution de la puissance de l'objet ») | + poids perdu |
| Échec sans effet | message `183` (« Malgré vos talents, la magie n'opère pas… ») | inchangé |

Le **résultat** vient du message affiché par le jeu (codes `INFOS_117`, `INFOS_183`, `INFOS_194` du fichier de langue) ; les **gains et pertes** viennent de la comparaison des stats. Retro recrée l'objet (nouvel identifiant) quand la rune modifie ses stats ; l'application suit l'objet posé dans l'atelier à travers ces versions. Le reliquat se calcule sur la variation **nette** de poids : un gain et une perte sur la même stat se compensent correctement. La rune consommée est rattachée au passage le plus proche. Le jeu envoie parfois deux messages pour une même rune (ex. 183 puis 194 dans la même seconde) : ils ne comptent que pour un passage, avec le résultat le plus précis (succès neutre, puis échec, puis échec sans effet). Plusieurs exemplaires du même objet sont distingués dans la liste par leurs caractéristiques et l'heure de la dernière rune.

Le poids d'une variation = nombre de points × poids par point de la stat. Les poids par défaut sont des valeurs communautaires ; le bouton **Poids des stats** les affiche à la place de l'historique, avec l'icône de la rune de chaque stat : modifiez une valeur puis Entrée (« Rétablir » ou « Tout rétablir » pour revenir aux valeurs par défaut). La nouvelle valeur compte pour les runes suivantes, sans redémarrer ; l'historique garde les anciens calculs. Les poids sont enregistrés dans `fm_poids.json`. Le reliquat reste une **estimation** : corrigez-le à la main si vous connaissez la vraie valeur. L'historique et le reliquat de chaque objet sont conservés entre deux lancements.

Pour que l'état « avant » de l'objet soit connu, lancez l'application **avant de vous connecter au jeu** (l'inventaire est alors capturé).

## Dépannage

Si la banque n'apparaît pas :

1. Cochez « Journaliser les paquets ».
2. Ouvrez la banque en jeu.
3. Lisez `%LOCALAPPDATA%\DofusRetroManager\packets.log`. Il ne contient que les messages de banque et d'inventaire (règle 4).
