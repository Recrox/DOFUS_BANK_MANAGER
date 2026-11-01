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

Au lancement, l'application vérifie si une version plus récente est publiée dans les Releases GitHub. Si c'est le cas, un bandeau s'affiche : **Télécharger et installer** récupère l'installateur dans l'application (avec sa progression), vérifie qu'il est intact (taille et empreinte SHA-256 publiées par GitHub), puis **Installer maintenant** ferme l'application et lance l'installation. Elle remplace l'ancienne version et conserve les banques sauvegardées, les prix et le thème ; à la fin, laissez cochée « Lancer Dofus Bank Manager » pour la rouvrir. Le bouton **Nouveautés** ouvre la page de la version sur GitHub.

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

Chaque push sur `main` publie une Release, via le workflow [release.yml](.github/workflows/release.yml) :

- **Correctif** : rien à faire. Si `<Version>` dans [DofusBankManager.csproj](DofusBankManager/DofusBankManager.csproj) n'est pas plus récente que la dernière Release, le dernier chiffre est augmenté automatiquement (`1.6.0` publiée → `1.6.1`).
- **Nouvelle fonctionnalité ou changement incompatible** : augmentez vous-même `<Version>` (`1.7.0`, `2.0.0`) avant de pousser ; c'est elle qui est publiée.

Le workflow construit l'installateur, crée le tag (ex. `v1.6.1`), publie la Release avec le `.msi`, puis **écrit la version publiée dans le `.csproj`** par un commit « Version 1.6.1 [skip ci] » (qui ne relance pas le workflow). **Faites donc `git pull` après chaque publication**, avant de committer à nouveau : sinon votre version locale reste l'ancienne et votre prochain push sera refusé. Suivez l'avancement dans l'onglet **Actions** du dépôt (environ 3 minutes). On peut aussi le relancer à la main : Actions › Release › **Run workflow**.

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
- Exception, pour les onglets Combat et TTG : sont aussi lus la map (`GDM`, `GM`), les caractéristiques du personnage (`As`), les fins de combat (`GE`), le groupe (`PM`, `PV`), les sorts du personnage (`SL`), l'entrée en combat (`GJK`) et les sorts lancés et attaques à l'arme (`GA` de type 300, 302 et 303, y compris ceux envoyés par le client du joueur, pour savoir quelle fenêtre joue). Ces messages contiennent aussi des noms de joueurs. On n'en garde que :
  - les groupes de monstres de la map (modèles, niveaux, étoiles) ;
  - les joueurs en mode marchand (pseudo et case) : pour une boutique consultée, ils sont enregistrés avec ses prix de cartes TTG, sur ce PC seulement, **jamais partagés** ;
  - aucun autre sprite (joueurs, PNJ…) ;
  - la sagesse et la prospection du personnage ;
  - la ligne du personnage en fin de combat (XP, kamas, objets) et les monstres affrontés ;
  - le niveau et la prospection des membres du groupe, jamais leur nom ;
  - les sorts du personnage et ceux qu'il lance en combat (arme comprise) et son identifiant, jamais ceux des autres combattants.

  Ces messages ne sont **jamais journalisés**, ni dans `packets.log` ni dans le journal de l'application, et rien n'en sort de la machine.
- **Aucune donnée du joueur ne sort de la machine** : pas de télémétrie, pas d'envoi de la banque ni de l'inventaire. Les seules requêtes réseau autorisées sont :
  - le téléchargement des données du jeu depuis le CDN d'Ankama ([GameData.cs](DofusBankManager/Data/GameData.cs)) ;
  - la lecture de la dernière Release GitHub du projet pour vérifier la version, et le téléchargement de son installateur quand l'utilisateur le demande ([UpdateChecker.cs](DofusBankManager/UpdateChecker.cs)) ;
  - le serveur de partage des prix du groupe ([HdvSync.cs](DofusBankManager/Hdv/HdvSync.cs), [server/](server/README.md)), **uniquement si l'utilisateur l'active** (désactivé par défaut). Il n'envoie que des prix : pour l'HDV, l'identifiant de l'objet, ses prix ×1/×10/×100, le prix moyen et la date du relevé ; pour les cartes TTG, l'identifiant de la carte, son prix, la quantité en vente et la date (jamais le marchand) ; pour les pierres d'âme, l'identifiant du monstre, la catégorie de pierre, le prix, le nombre d'offres, le prix moyen et la date.

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
- **Données du jeu** ([Data/GameData.cs](DofusBankManager/Data/GameData.cs)) : les fichiers `items`, `crafts`, `skills`, `jobs`, `effects`, `monsters` et `maps` (coordonnées et zones) sont décodés par un mini-interpréteur AS2 ([Data/Avm1.cs](DofusBankManager/Data/Avm1.cs)).
- **Forgemagie** ([Fm/FmTracker.cs](DofusBankManager/Fm/FmTracker.cs)) : voir ci-dessous.

Les fichiers sont stockés dans `%LOCALAPPDATA%\DofusBankManager` : cache des langues, banques sauvegardées, `settings.json` (thème), `fm.json` (historique FM), `fm_poids.json` (poids des stats), `hdv.json` (prix relevés), `pda.json` (prix des pierres d'âme par monstre), `marchands.json` (prix des cartes TTG chez les marchands), `chasse.json` (archi-monstres aperçus et combats) et `packets.log`.

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

Les **prix marchands** se relèvent quand vous ouvrez la boutique d'un joueur en mode marchand : l'application lit la liste des objets en vente (`ECK4` puis `EL…`) et retient, pour chaque carte, le prix le plus bas de cette boutique, avec le **pseudo du marchand**, sa **map** (coordonnées et zone) et sa **case**, lus sur la map. Ces trois informations restent sur votre PC et ne sont **jamais partagées**. La liste affiche, pour chaque carte, le marchand et la position de la meilleure offre de la période (colonnes « Chez » et « Position »). Une même carte chez un même marchand n'est relevée qu'une fois par heure. Comme pour l'HDV, rien n'est demandé au serveur : seules les boutiques que vous consultez donnent un prix. Si le partage entre amis est activé (onglet HDV), ces prix sont partagés avec le groupe, sans le marchand ; les relevés venus du groupe sont marqués « groupe ».

Le panneau de détail liste, au choix, les **offres vues sur la période** ou **tout l'historique** de la carte, chaque relevé avec son marchand et sa position. Par défaut, les **offres vues sur la période** (7 jours par défaut, champ « Période »), de la moins chère à la plus chère, avec le marchand, sa position et la date ; puis l'évolution du prix chez les marchands et à l'HDV. Les relevés d'avant cette version, et ceux venus du groupe, n'ont pas de marchand. L'onglet HDV affiche aussi un graphique de l'évolution du prix unitaire pour l'objet sélectionné.

### Onglet PDA (pierres d'âme)

L'HDV des pierres d'âme ne liste pas les offres par objet mais **par monstre** : chaque pierre pleine (de monstre, de gardien de donjon ou d'archi-monstre) est vendue sous les monstres qu'elle contient. L'application relève donc, pour chaque monstre consulté, la pierre la moins chère, le prix moyen, le nombre d'offres et la date. Ces prix ne vont pas dans l'onglet HDV ; ils sont partagés avec le groupe si le partage entre amis est activé.

Une même liste mélange les catégories : celle d'un Bulbuisson contient des pierres de monstre, mais aussi des pierres d'archi-monstre où il accompagne l'archi (Bambouské le Camouflé). Les prix sont donc tenus **par monstre et par catégorie** (Monstre, Donjon, Archi-monstre), et une pierre d'archi-monstre ne compte que pour l'archi-monstre lui-même.

- **Prix relevés à l'HDV** : un monstre par ligne, avec sa race, son niveau et la catégorie de pierre (Monstre, Donjon, Archi-monstre). « Tous les monstres du jeu » ajoute ceux dont le prix n'a pas encore été relevé.
- **Mes pierres** : vos pierres pleines (banque et inventaire) avec leur contenu. La valeur estimée d'une pierre est le prix relevé le plus haut parmi ses monstres.

Le panneau de détail montre l'évolution du prix du monstre sélectionné. Les noms des monstres viennent du fichier de langue `monsters`.

### Onglet Combat

L'onglet affiche les **groupes de monstres de la map** où vous êtes (monstres, niveau du groupe, étoiles), votre **niveau**, votre **sagesse**, votre **prospection**, et le niveau et la prospection du **groupe** (somme des membres, lue dans les messages de groupe ; les vôtres seuls hors groupe).

- **Multi-compte** : chaque fenêtre de jeu est suivie séparément. La liste « Personnage » choisit celle à afficher ; « Auto » suit la fenêtre qui vient de changer de map et affiche son personnage. En combat, au début du tour d'un de vos personnages, ou dès qu'il lance un sort ou attaque, l'onglet passe sur lui (même s'il a été choisi à la main). Si l'application a démarré après la connexion, son premier sort ou sa première attaque suffit à le reconnaître. La rentabilité est celle du personnage affiché, avec le total de tous les personnages en dessous. Une fenêtre fermée ou déconnectée disparaît de la liste ; après une reconnexion, le personnage reprend sa place.
- **Lancez l'application avant de choisir votre personnage** : c'est à ce moment qu'elle apprend son identifiant, nécessaire pour retrouver sa ligne dans les résultats de combat. Sinon, seuls les combats en solo sont comptés (revenir à la sélection des personnages suffit), et le nom du personnage n'apparaît qu'après son premier combat en solo. De même, la sagesse et la prospection ne sont envoyées par le jeu qu'à la connexion ou quand elles changent : changer une pièce d'équipement suffit à les faire apparaître.

- **Archi-monstres** : quand un groupe contenant un archi-monstre apparaît sur la map, une alerte sonore retentit (case « Alerte sonore archi-monstre »). L'historique liste les derniers archi-monstres vus, avec leur niveau, leur **position** (coordonnées et zone de la map) et le dernier prix relevé de leur pierre (onglet PDA). Un même archi sur la même map n'est signalé qu'une fois toutes les 10 minutes.
- **XP et drops estimés** : les données du jeu ne contiennent ni l'XP ni les drops des monstres. L'application les **apprend de vos fins de combat** : l'XP gagnée est ramenée sans sagesse, puis répartie entre les monstres selon leur niveau ; les objets obtenus sont attribués aux monstres du combat, d'où une fréquence observée. Pour un groupe de la map, l'XP estimée applique votre sagesse actuelle et les étoiles du groupe. Un monstre encore jamais combattu prend l'XP par niveau moyenne des monstres déjà appris (valeur précédée de « ≈ ») ; avant tout combat, les colonnes affichent « à apprendre ». Ce sont des **estimations** : elles se précisent au fil des combats, et ne tiennent compte ni de la taille du groupe ni de l'écart de niveau.
- **Rentabilité** : la session additionne l'XP, les kamas et les drops (au prix unitaire HDV) de vos combats, puis projette ce rythme sur 30, 60 et 120 minutes. « Nouvelle session » repart de zéro ; après plus d'une heure sans combat, une nouvelle session s'ouvre d'elle-même.

- **Économe** (challenge : chaque sort une seule fois par combat) : l'onglet affiche les sorts du personnage, et son arme (corps-à-corps, qui compte comme un sort), avec leurs icônes du jeu (fond, cadre et symbole lus dans le client installé, couleurs du fichier de langue des sorts) : normales tant qu'ils sont disponibles, grisées une fois lancés (comme un sort qu'on ne peut pas lancer), grisées et encadrées de rouge s'ils sont lancés deux fois (challenge raté). La liste repart de zéro à chaque entrée en combat (ou avec « Remettre à zéro »). Un échec critique compte comme un sort lancé. Les sorts du personnage sont envoyés par le jeu à la connexion, puis gardés d'une fois sur l'autre.

Comme le reste, l'onglet ne fait qu'afficher : c'est toujours le joueur qui se déplace, qui choisit ses sorts et qui combat (règle 3).

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
