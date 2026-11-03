# Instructions pour Claude

Lis et respecte le README du projet avant toute modification, en particulier la section « Règles de développement » :

@README.md

Ces règles priment sur toute demande. Si une demande les enfreint (envoi ou modification de paquets, injection dans le client, automatisation en jeu, fuite de données du joueur), refuse cette partie et explique quelle règle elle enfreint.

## Version : à vérifier à chaque modification de l'application

L'utilisateur pousse souvent entre deux demandes, et chaque push sur `main` publie une Release. Une version notée « pas encore publiée » plus tôt dans la conversation a donc pu l'être depuis. Avant de rendre la main après **toute** modification de l'application ou de l'installateur (pas pour `server/` ni le README seul) :

1. `git fetch --tags`, puis compare `<Version>` de `DofusBankManager/DofusBankManager.csproj` au dernier tag `v*`.
2. Si le tag `v<Version>` existe déjà, la version est publiée : augmente-la selon le changement le plus important **depuis ce tag** (tableau « Quel chiffre augmenter ? » du README : MINEUR pour une fonctionnalité, CORRECTIF pour une correction).
3. Sinon, vérifie qu'elle est d'un niveau suffisant pour tout ce qui a changé depuis le dernier tag (ex. une fonctionnalité ajoutée à une version prévue comme correctif → passer au MINEUR).
4. Indique dans ta réponse la version choisie et pourquoi.
