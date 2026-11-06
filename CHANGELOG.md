# Nouveautés

Les changements de chaque version, la plus récente en haut. Ce fichier est intégré à l'application (bouton **Nouveautés** en haut) et sert de notes de Release sur GitHub.

## 1.47.0 — 6 novembre 2026

- **HDV** : nouveau bouton « 📊 Mes échanges » : vos achats (HDV et boutiques de marchands), vos mises en vente, vos ventes (y compris celles faites hors connexion, constatées à la réouverture du mode vente) et vos retraits, avec les totaux sur la période, la taxe payée, le bilan et les objets les plus échangés. Filtre par personnage.
- **Brisage** : les caractéristiques s'affichent en icônes de runes avec leur valeur, plus compactes et lisibles ; le détail au survol.
- **Brisage** : pour les objets du jeu, la fourchette (ex. Vitalité 351–400) s'affiche dès qu'un exemplaire a été vu à l'HDV ou dans vos banques : le jeu ne donne que le maximum.
- **Brisage** : filtres % résistance neutre, terre, feu, eau et air.
- **Recettes** : nombre de cases de chaque recette dans la liste.
- **Switcheur** : Correction : application relancée pendant le jeu, le personnage est aussi reconnu dès qu'il joue en combat (déplacement, sort, fin de tour) : le temps du tour s'affiche sans relancer les fenêtres Dofus.

## 1.46.0 — 6 novembre 2026

- **Interface** : l'application s'ouvre toujours dans l'interface moderne (HTML) ; l'ancienne interface et le bouton « Interface classique » sont retirés, ce qui rend l'application plus légère. Les styles Classique et Moderne et les trois thèmes restent au choix en haut de la page.
- **Carte** : la carte se charge en arrière-plan, sans figer l'application.
- **Performance** : au repos, l'application n'occupe plus le processeur (un fond animé du style Moderne en occupait un cœur entier en permanence) ; la page n'est mise à jour que quand quelque chose change, et seulement pour l'onglet affiché, sans recharger les images.
- **Installation** : sans Microsoft Edge WebView2 (fourni avec Windows 11), un message propose d'ouvrir sa page de téléchargement.

## 1.45.0 — 6 novembre 2026

- **PDA et Banque** : Correction : quand l'application démarre après le jeu, les objets d'un personnage ne sont plus attribués à un autre (pierres d'âme, archi-monstres affichés chez le mauvais personnage). Reconnectez une fois un personnage dont l'inventaire paraît faux pour le remettre d'aplomb.
- **Switcheur** : Correction : application relancée pendant que le jeu est ouvert, la bascule au début du tour et le temps du tour marchent sans relancer les fenêtres Dofus (dès le premier changement de map).
- **Switcheur** : le clic + touche marche aussi avec le clic droit (au choix : gauche, droit ou les deux).
- **Switcheur** : option « Anti-rebond fenêtre » : si Windows rend la main à la fenêtre quittée, la fenêtre demandée est réaffichée.
- **Switcheur** : Correction : les fenêtres du jeu ne clignotent plus dans la barre des tâches avec le clic + touche.
- **Superposition** : l'ordre de jeu est tout en haut et ne bouge plus quand les autres blocs changent.
- **HDV** : le partage entre amis et « Synchroniser » sont disponibles dans l'interface HTML.
- **TTG** : le classeur est plus grand et affiche le prix de chaque carte, le prix du paquet de chaque famille et le coût des cartes manquantes ; nom, état et prix dès le survol.
- **Carte** : un clic sur une case affiche sa zone, ses points d'intérêt, ses maisons et les ressources vues sur ses maps (nombre disponible et repousse).
- **Combat** : bouton « Pause » pour la rentabilité de la session quand vous vous absentez ; un combat la relance.

## 1.44.0 — 5 novembre 2026

- **TTG** : nouveau bouton « 📖 Classeur » : votre collection présentée comme le livre du jeu. Avancement par type (classeur, brillantes, familles complètes) et par rareté, une icône par famille avec sa progression, puis les cartes de la famille : en couleur si elles sont rangées au classeur, pâles si elles sont en banque ou dans l’inventaire, en pointillés si elles manquent (prix au survol). Les cartes brillantes ont leur propre page.

## 1.43.0 — 5 novembre 2026

- **Switcheur** : option « Clic + Ctrl » : cliquez dans le jeu en gardant Ctrl enfoncé (ou Alt, Maj) ; votre clic part normalement, puis la fenêtre suivante dans l’ordre de jeu s’affiche. Pratique pour déplacer vos personnages l’un après l’autre.
- **Switcheur et Combat** : plus besoin de relancer le jeu après une mise à jour ou si l’application démarre après Dofus : chaque fenêtre retrouve son personnage, avec son ordre de jeu, son niveau et sa vie, dès le premier message du jeu (changement de map, action…).

## 1.42.0 — 5 novembre 2026

- **Brisage** : filtres plus compacts : un bouton par caractéristique, avec l’icône de sa rune (Vi, Sa, Fo…) ; les champs minimum et maximum n’apparaissent que pour celles choisies.
- **Brisage** : les caractéristiques des objets s’affichent en entier, sans faire défiler le tableau de côté (type et niveau sous le nom, runes sous leur valeur).
- **Carte** : « Récolte sur la map » affiche l’icône de chaque ressource avec son nombre et sa repousse ; le nom au survol. Même chose dans la superposition en jeu.
- **PDA et Banque** : Correction : l’inventaire de chaque personnage est à jour sans ouvrir de banque, et en multi-compte chaque fenêtre de jeu garde son propre inventaire.
- **Combat** : Correction : la liste des derniers archi-monstres tient dans son cadre dans l’interface classique (bouton « Vider » toujours visible), et les trois listes de droite s’affichent en entier dans l’interface HTML.

## 1.41.0 — 4 novembre 2026

- **Combat** : Correction : dans l’interface HTML, la liste des derniers archi-monstres n’est plus coupée : position et date raccourcies (texte complet au survol), niveau sous le nom.

## 1.40.0 — 4 novembre 2026

- **Combat** : nouvelle liste « Drops de la session » : les objets obtenus, leur quantité, leur prix et le total ; un clic sur un combat n’affiche que ses drops.
- **Combat** : option « Pop-up archi-monstre » : une bulle au premier plan dès qu’un archi-monstre apparaît sur la map, avec le prix de sa pierre.
- **Combat** : nouvelles « Alertes monstres » : choisissez un monstre, un nombre minimum dans le groupe et des niveaux (ex. 3 Craqueleurs des plaines de niveau 49 au plus) ; un son différent de celui des archi-monstres vous prévient dès qu’un groupe de la map y répond.
- **Arène** : choisissez les personnages comptés, tous ou un groupe que vous créez (ex. vos 8 comptes principaux), pour qu’un autre compte ne fausse pas le chronomètre.

## 1.39.0 — 4 novembre 2026

- **Switcheur** : option « Agrandir la fenêtre au démarrage de Dofus » : chaque nouvelle fenêtre du jeu est agrandie dès qu’elle s’ouvre.
- **Switcheur** : nouveau « Mode split » pour jouer avec les fenêtres côte à côte : plus de bascule au début du tour ; dans la superposition, la ligne du personnage qui joue clignote avec le temps de son tour (« 12 / 30 s »).
- **Superposition** : un clic sur un personnage de l’ordre de jeu affiche sa fenêtre.
- **Superposition** : hors combat, la vie des personnages remonte d’elle-même, sans attendre de manger du pain (estimation).
- **Switcheur** : Correction : avec des fenêtres à la sélection du personnage, les raccourcis passent bien de l’une à l’autre au lieu de rester bloqués sur les deux premières.

## 1.38.0 — 4 novembre 2026

- **Recettes** : un clic sur un ingrédient (son nom ou son icône) dans le détail d’une recette ouvre l’onglet HDV sur cet objet.
- **Brisage** : un clic sur le nom d’un objet, ou le bouton « Voir la recette » du détail, ouvre sa recette dans l’onglet Recettes.

## 1.37.0 — 4 novembre 2026

- **Ocre** : nouvel onglet pour la quête du Dofus Ocre : ses 34 étapes et les âmes à rapporter, comparées aux pierres d’âme de votre banque et de votre inventaire. Il indique les étapes que vous pouvez valider, et pour chaque étape les âmes qui manquent (monstres, boss, archi-monstres) avec leur prix relevé à l’HDV et le total en kamas. Choisissez votre personnage et l’étape où vous en êtes ; « Coût de » sépare la valeur des boss de celle des archi-monstres.
- **TTG** : les cartes rangées dans votre classeur comptent maintenant dans la collection et les manquantes (nouvelle colonne « Classeur ») : ouvrez votre classeur en jeu, application lancée, pour qu’il soit relevé.
- **TTG** : une liste « Personnage » choisit de qui compter les cartes, ou de tous vos personnages, et « Manquantes » donne le nombre de cartes qui vous manquent et leur coût estimé d’après les prix relevés.

## 1.36.0 — 4 novembre 2026

- **Carte** : nouvelles **ressources** : choisissez un métier (Bûcheron, Pêcheur…) puis cochez les ressources voulues, avec leur icône, pour les voir sur la carte avec leur nombre par map. Seules les maps que vous avez visitées avec l’application lancée sont connues : la carte se complète au fil de vos déplacements.
- **Carte** : un bloc « Récolte sur la map » liste les ressources de votre map, disponibles et récoltées (« Petits poissons (mer) ×4 »), avec une estimation de leur réapparition, apprise en les voyant repousser.
- **Switcheur** : nouveau bloc « Récolte » dans la superposition en jeu, avec les ressources de la map de la fenêtre au premier plan.

## 1.35.0 — 4 novembre 2026

- **PDA** : dans « Mes pierres », une liste « Personnage » choisit de qui afficher les pierres (banque et inventaire), ou de tous vos personnages. Les personnages qui ne sont pas connectés sont repris de leur dernière banque connue ; une banque commune à plusieurs personnages d’un même compte n’est comptée qu’une fois.
- **Banque** : une liste « Personnage » affiche la banque du personnage en jeu ou la dernière banque connue d’un autre de vos personnages, avec sa date.

## 1.34.0 — 4 novembre 2026

- **Logo** : nouveau logo Dofus Retro Manager (l’œuf « DRM »), partout : en haut de l’application (interface classique et HTML), barre de titre, barre des tâches, menu Démarrer, Bureau et installateur.

## 1.33.1 — 4 novembre 2026

- **HDV** : Correction : dans l’historique des relevés, les lots ×1, ×10 et ×100 sont en gras et bien séparés de leur prix (« ×1 1 658 k » ne se lit plus « ×11 658 k »).

## 1.33.0 — 4 novembre 2026

- **Nouveau nom** : Dofus Bank Manager devient **Dofus Retro Manager**, avec une nouvelle icône « DRM ». Vos banques, prix, réglages et historiques sont repris automatiquement ; les raccourcis du menu Démarrer et du Bureau sont remplacés.

## 1.32.0 — 3 novembre 2026

- **Interface HTML** : nouvelles icônes des onglets, plus lisibles (coffre pour la Banque, épées pour le Combat, baguette pour la FM…), et une infobulle qui explique chaque onglet.

## 1.31.0 — 3 novembre 2026

- **Switcheur** : réglages réorganisés en cartes (Raccourcis, Notifications, Bascules automatiques, Superposition en jeu), avec des interrupteurs expliqués et des pastilles à cocher. « Masquer les réglages » laisse toute la place à la liste (interface HTML).
- **FM** : le bouton « Poids des stats » affiche les poids dans l'application, avec l'icône de chaque rune, à la place de l'historique. Modifiez une valeur puis Entrée : elle compte tout de suite pour les runes suivantes, sans redémarrer. « Rétablir » revient à la valeur par défaut.
- Correction : dans l'interface HTML, « Oublier l'objet » et « Effacer l'historique » (onglet FM) demandent confirmation dans la page et fonctionnent.

## 1.30.0 — 3 novembre 2026

- **Switcheur** : chaque personnage est affiché avec sa tête, selon sa classe et son sexe, et sa classe sous son nom (interface classique et HTML). La tête apparaît aussi dans la superposition en jeu. La première fois, lancez l'application avant de choisir vos personnages.

## 1.29.0 — 3 novembre 2026

- **Mises à jour** : une nouvelle version publiée pendant que l'application est ouverte est signalée sans avoir à la relancer (vérification toutes les 30 secondes). « Plus tard » masque le bandeau jusqu'à la version suivante.

## 1.28.0 — 3 novembre 2026

- **Positions cliquables** : un clic sur la position d'un marchand (onglet TTG), d'un archi-monstre ou sur votre map actuelle (onglet Combat) ouvre la carte centrée sur la bonne case, avec un repère.

## 1.27.0 — 3 novembre 2026

- **Mises à jour** : un clic sur « Mettre à jour » suffit. L'application télécharge la nouvelle version, l'installe sans assistant (rien à accepter), puis se rouvre d'elle-même sur ses nouveautés. Valable à partir de la mise à jour suivante.
- **Mises à jour** : si l'installation échoue, l'ancienne version se rouvre et explique pourquoi.

## 1.26.0 — 3 novembre 2026

- **Nouveautés** : un bouton en haut de l'application affiche ce journal des changements, version par version (interface classique et HTML).

## 1.25.0 — 3 novembre 2026

- **Arène** : le chronomètre et les challenges Économe et Barbare sont maintenant aussi dans l'interface HTML.
- **Premier lancement** : l'application s'ouvre en interface HTML, style Moderne. Ceux qui l'ont déjà gardent leur choix.

## 1.24.0 — 3 novembre 2026

- **FM** : quand le jeu envoie deux messages pour une même rune (ex. « la magie n'opère pas » puis « succès neutre »), un seul passage est compté, avec le résultat le plus précis.
- **FM** : la liste des objets indique leurs caractéristiques et l'heure de la dernière rune, pour distinguer plusieurs exemplaires du même objet.

## 1.22.1 — 3 novembre 2026

- **Carte** dans l'interface HTML, avec filtres, recherche et position de vos personnages.
- **Interface HTML** : le style (Classique, Moderne) et le thème (Jour, Crépuscule, Nuit) se changent en haut de la page.
- Correction : l'application ne démarrait plus (onglet Carte).
- Correction : l'interface HTML ne se rouvrait pas au lancement.

## 1.22.0 — 3 novembre 2026

- **Nouvel onglet Carte** : carte du monde (continent et Incarnam) avec zaaps, temples, hôtels de vente, ateliers, donjons, maisons… lue dans le client installé.
- **Nouvel onglet Arène** : chronomètre (combats par heure, gain moyen, XP par heure) et suivi des challenges Économe et Barbare.
- **Switcheur** : case pour l'activer ou le désactiver, bascule sur la fenêtre du personnage dont c'est le tour, ordre figé à l'entrée en groupe, anti-rebond des raccourcis (160 ms).
- **Superposition** : suit la fenêtre au premier plan ; affiche niveau et prospection du personnage et du groupe, sagesse, et la barre de vie de chaque personnage.
- **Banque et PDA** : le nom du personnage est affiché (« Banque de … », « Inventaire de … »).

## 1.21.0 — 2 novembre 2026

- L'onglet Fenêtres devient **Switcheur**.
- **Ordre personnalisé** par glisser-déposer, et **personnage principal** (étoile) avec son propre raccourci.
- **Superposition en jeu** : personnage, Économe, groupes de la map et ordre de jeu, au-dessus du jeu.

## 1.20.0 — 2 novembre 2026

- **Deux styles** (Classique, Moderne) et nouvelle police.
- **Interface HTML** (bouton « HTML » en haut).
- **Nouvel onglet Fenêtres** (multi-compte) : raccourcis pour passer d'un personnage à l'autre dans l'ordre d'initiative, notifications (combat, tour, échange, invitation de groupe).
- Nouveau son pour l'alerte archi-monstre, avec réglage du volume.

## 1.19.0 — 1er novembre 2026

- L'XP de base de 1 063 monstres est livrée avec l'application : l'XP estimée des groupes s'affiche dès le premier lancement.

## 1.11.0 — 1er novembre 2026

- L'onglet Chasse devient **Combat**.
- **Économe** : sorts et arme du personnage, avec les icônes du jeu, grisés une fois lancés.
- **Drops** : taux de drop officiels du jeu, chance selon la prospection du groupe et les étoiles, valeur estimée par combat.
- **Banque** : prix unitaire et prix moyen de l'HDV.
- **TTG** : historique complet des relevés d'une carte.
- Versions 1.11.1 à 1.11.8 : corrections.

## 1.10.0 — 1er novembre 2026

- **TTG** : position des marchands (coordonnées, zone et case) et offres vues sur la période.
- Coordonnées et zones des maps.

## 1.9.0 — 1er novembre 2026

- **Multi-compte** dans l'onglet Chasse : chaque fenêtre de jeu est suivie séparément ; niveau du personnage et du groupe.

## 1.8.0 — 1er novembre 2026

- **Nouvel onglet Chasse** : groupes de monstres de la map, alerte archi-monstre, XP et drops estimés, rentabilité de la session.

## 1.7.0 — 1er novembre 2026

- Publication automatique des versions.
- Améliorations des onglets TTG et PDA.

## 1.6.0 — 1er novembre 2026

- **Mises à jour** : un bandeau signale une nouvelle version ; elle se télécharge et s'installe depuis l'application.

## 1.5.0 — 1er novembre 2026

- **Recettes** : coût, prix de vente, bénéfice et sous-recettes.
- **Nouveaux onglets Brisage, TTG (cartes) et PDA (pierres d'âme)**.

## 1.4.0 — 1er novembre 2026

- Affichage de la version de l'application.

## 1.3.1 — 1er novembre 2026

- Partage des prix HDV entre amis quasi immédiat.

## 1.3.0 — 1er novembre 2026

- **Nouveaux onglets HDV** (prix relevés) **et FM** (forgemagie et reliquat).
- **Partage des prix entre amis** (désactivé par défaut).

## 1.1.0 — 31 octobre 2026

- Installateur Windows.
