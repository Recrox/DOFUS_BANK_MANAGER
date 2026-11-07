# Règles de développement

Ces règles ne sont pas négociables : elles garantissent que Dofus Retro Manager reste un simple outil de lecture. Voir aussi le [README](README.md).

## 1. Lecture seule, toujours

L'application **observe**. Elle n'agit jamais sur le jeu.

- **Ne jamais envoyer de paquet** au serveur Dofus, ni en ouvrir une connexion.
- **Ne jamais modifier, bloquer ou rejouer** un paquet. Pas de proxy, pas de redirection du client (pas de modification du `config.xml`, du fichier `hosts`, etc.).
- La capture reste passive : une socket brute en réception (`SIO_RCVALL`), et rien d'autre.

## 2. Ne pas toucher au client

- **Pas de lecture ni d'écriture de la mémoire** du processus Dofus.
- **Pas d'injection** de code ou de DLL, pas de hook.
- **Pas de modification des fichiers** installés du jeu (`%LOCALAPPDATA%\Ankama\Retro`). Les lire, comme le `config.xml`, est autorisé.

## 3. Aucune automatisation en jeu

- **Pas de clics, de touches ou de déplacements simulés**, pas de macro.
- L'application **affiche** des informations. C'est toujours le joueur qui agit, à la main.
- Gestion des fenêtres (onglet Switcheur) : l'application peut **mettre au premier plan** une fenêtre du jeu, comme Alt+Tab : quand le joueur le demande (raccourci, clic sur une bulle ou un bouton, ou, sur option, clic dans le jeu avec une touche enfoncée : son clic arrive au jeu sans être bloqué ni rejoué, puis la fenêtre suivante s'affiche ; l'application relit seulement l'état du bouton et de la touche, sans hook), ou, si l'option est activée, quand un de ses personnages reçoit une invitation de groupe ou une demande d'échange (c'est toujours le joueur qui accepte), ou quand commence le tour de jeu d'un de ses personnages en combat (c'est toujours le joueur qui joue). Sur option, elle peut aussi **agrandir** une fenêtre du jeu qui vient de s'ouvrir (comme son bouton Agrandir). Elle ne lit que le titre des fenêtres et n'envoie **jamais** de touche, de clic ni de message à une fenêtre du jeu. Pas de « suivre le chef », pas de diffusion d'une touche à plusieurs fenêtres.
- Une fonctionnalité qui ferait jouer à la place du joueur (crafter, déposer, vendre automatiquement) est **refusée**, même « juste pour tester ».

C'est ce qui sépare un outil d'aide d'un bot. Les sanctions d'Ankama visent surtout cette catégorie.

## 4. Protéger les données du joueur

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
  - l'identifiant du personnage, retrouvé sur la map (`GM`) quand l'application est relancée pendant que le jeu est ouvert : le nom de chaque joueur de la map est seulement comparé à celui du personnage de la fenêtre (lu dans son titre), rien d'autre n'en est lu ni gardé ;
  - l'initiative du personnage (As, onglet Switcheur : ordre de jeu) ;
  - la classe et le sexe de vos personnages (`ASK`, déjà lu pour l'inventaire ; onglet Switcheur : icône), gardés dans `chasse.json` ;
  - le dernier état connu de vos personnages (identifiant, niveau, initiative, sagesse, prospection, points de vie et rythme de régénération), gardé dans `chasse.json` pour les retrouver quand l'application est relancée pendant que le jeu est ouvert (mise à jour…) ;
  - les points de vie du personnage (As, et en combat `GTM` : seule la ligne du personnage, celles des autres combattants sont ignorées) ;
  - la durée du tour de jeu (`GTS`, déjà lu pour le début du tour) et le rythme de régénération des points de vie hors combat (`ILS` : un intervalle en millisecondes), pour le temps du tour et la vie estimée de l'onglet Switcheur ;
  - l'identifiant des challenges du combat et leur résultat (`Gd`, onglet Combat : chronomètre), rien d'autre.
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
- Pour « Mes échanges » (onglet HDV) sont aussi lus, en plus des messages d'HDV et d'inventaire déjà lus : l'ouverture et la fermeture des échanges (`ECK`, `EV` : type, tailles des lots et taxe de l'HDV), vos offres en mode vente de l'HDV (`EL`, `EmK` : objet, quantité, prix), la boutique d'un marchand ouverte (`EL`, déjà lue pour les cartes TTG) et les messages d'information de vente à l'HDV (`Im` 65 et 73 : objet vendu, en jeu ou hors jeu, quantité et kamas reçus ; `Im` 67 : vente expirée, objet et quantité), qui ne contiennent aucun nom de joueur et ne sont pas écrits dans `packets.log`. Un achat est reconnu quand un lot disparaît d'une offre consultée et que l'inventaire gagne la même quantité au même moment. On n'en garde, dans `echanges.json`, que la date, le personnage, l'objet, la quantité, le prix et l'état de l'offre : **jamais le vendeur, l'acheteur ni le marchand**, et rien n'est partagé.
- Pour le temps du tour (onglet Switcheur), sont aussi lus le déplacement (`GA001`) et la fin de tour (`Gt`) envoyés par le client du joueur : seul le fait qu'il joue est lu (pas le chemin), pour reconnaître le personnage de la fenêtre dont c'est le tour.
- Pour le classeur de cartes (onglet TTG) sont aussi lus le contenu du classeur, envoyé à son ouverture (`td` : identifiant du personnage et cartes rangées, avec leur date d'ajout), et la carte ajoutée (`ta` : identifiant de la carte). On n'en garde que les identifiants des cartes, avec la banque du personnage (`banks/`) ; ces messages ne sont jamais journalisés.

## 5. Données du jeu

- Les fichiers de langue (objets, recettes, métiers) appartiennent à Ankama. Ils sont **téléchargés depuis leur CDN et mis en cache localement**. **Ne pas les committer ni les redistribuer.**
- Données tierces : seules celles que le mainteneur est autorisé à utiliser peuvent être livrées avec l'application, comme l'XP des monstres ([Data/monstres-xp.json](DofusRetroManager/Data/monstres-xp.json), réduite à l'identifiant, au niveau et à l'XP). Ne jamais récupérer de données en masse sur un site qui l'interdit (robots.txt, conditions d'utilisation).
- Rester sobre : un fichier n'est téléchargé que si sa version a changé (voir [GameData.cs](DofusRetroManager/Data/GameData.cs)). Pas de requêtes en boucle.

## 6. Rappel

Même passif, cet outil est un logiciel tiers. Les conditions d'utilisation d'Ankama peuvent l'interdire, et aucune technique ne garantit l'absence de détection. **Chacun l'utilise à ses propres risques.** Respecter les règles ci-dessus réduit le risque, sans l'annuler.

**En cas de doute sur une fonctionnalité, la réponse par défaut est non.**
