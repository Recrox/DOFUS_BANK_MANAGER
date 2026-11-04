# Instructions pour Claude

Lis et respecte le README du projet avant toute modification, en particulier la section « Règles de développement » :

@README.md

Ces règles priment sur toute demande. Si une demande les enfreint (envoi ou modification de paquets, injection dans le client, automatisation en jeu, fuite de données du joueur), refuse cette partie et explique quelle règle elle enfreint.

## Version : à vérifier à chaque modification de l'application

L'utilisateur pousse souvent entre deux demandes, et chaque push sur `main` publie une Release. Une version notée « pas encore publiée » plus tôt dans la conversation a donc pu l'être depuis. Avant de rendre la main après **toute** modification de l'application ou de l'installateur (pas pour `server/` ni le README seul) :

**Règle du projet : on augmente toujours le MINEUR** (`1.x.0`), jamais le seul CORRECTIF, même pour une simple correction : `1.33.0` → `1.34.0`, puis `1.35.0`… Le 3e chiffre reste à `0` (seul le workflow de Release l'augmente, si personne n'a changé la version avant de pousser). Le MAJEUR ne change que pour un changement incompatible (tableau « Quel chiffre augmenter ? » du README). Cette règle prime sur la distinction fonctionnalité / correction du README.

1. `git fetch --tags`, puis compare `<Version>` de `DofusRetroManager/DofusRetroManager.csproj` au dernier tag `v*`.
2. Si la version du `.csproj` n'est pas plus récente que le dernier tag (tag `v<Version>` existant, ou version égale ou inférieure) : passe au **MINEUR suivant du dernier tag**, CORRECTIF à 0 (dernier tag `v1.33.1` → `1.34.0`).
3. Sinon (déjà augmentée, pas encore publiée) : vérifie qu'elle a bien la forme `1.x.0`, avec x = MINEUR du dernier tag + 1 ; corrige-la sinon (ex. `1.33.2` → `1.34.0`).
4. Indique dans ta réponse la version choisie et pourquoi.

## Journal des changements : à tenir à chaque modification de l'application

`CHANGELOG.md` (racine) est affiché dans l'application (bouton « Nouveautés ») et sert de notes à la Release. Après **toute** modification visible de l'application ou de l'installateur, une fois la version vérifiée (section précédente) :

1. Ajoute une ligne dans la section `## <Version> — <date>` de la version du `.csproj`. Crée la section en haut du fichier si elle n'existe pas (version tout juste augmentée), avec la date du jour en toutes lettres (« 3 novembre 2026 »).
2. Rédige pour le joueur, en français, une puce par changement : `- **Onglet ou sujet** : ce qui change pour lui.` Pas de nom de fichier, de classe ni de détail technique. Une correction commence par « Correction : ».
3. Ne modifie jamais la section d'une version déjà publiée (tag `v<version>` existant) : ses notes sont figées.
4. Le format est lu par [Changelog.cs](DofusRetroManager/Changelog.cs) et par le workflow de Release : titres `## x.y.z — date`, puces `- `, gras `**…**` seulement.
