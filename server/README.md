# Serveur de partage des prix HDV

Petite base de prix commune, hébergée gratuitement sur Cloudflare (Workers + D1). Elle permet à un groupe d'amis de mettre en commun les prix relevés à l'hôtel de vente : chacun consulte quelques objets, tout le monde reçoit tous les prix.

Elle ne stocke **que** : identifiant d'objet, prix des lots ×1/×10/×100, prix moyen, date du relevé. Aucune donnée de joueur (voir la règle 4 du README principal).

L'accès (lecture et écriture) exige le **code de groupe**, un secret stocké côté Cloudflare, jamais dans le code. Le dépôt peut donc rester public.

## Déployer (une seule fois, ~10 minutes)

Prérequis : Node.js et un compte Cloudflare gratuit (https://dash.cloudflare.com/sign-up, aucune carte bancaire).

Depuis le dossier `server` :

```powershell
npm install
npx wrangler login                     # ouvre le navigateur pour connecter votre compte Cloudflare
npx wrangler d1 create dbm-hdv         # crée la base ; copiez le database_id affiché dans wrangler.toml
npx wrangler d1 execute dbm-hdv --remote --file=schema.sql
npx wrangler secret put GROUP_KEY      # choisissez le code de groupe (long et difficile à deviner)
npx wrangler deploy                    # affiche l'adresse : https://dofus-bank-manager-hdv.<compte>.workers.dev
```

Ensuite, dans l'application : onglet **HDV** › **Partage entre amis** › collez l'adresse et le code, cochez **Activer le partage**, puis **Tester** et **Enregistrer**.

Donnez l'adresse et le code à vos amis **en privé** (message privé, pas sur un salon public) : toute personne qui a les deux peut lire et écrire dans la base.

## Changer le code de groupe

```powershell
npx wrangler secret put GROUP_KEY
```

L'ancien code cesse de fonctionner immédiatement ; redonnez le nouveau aux membres du groupe.

## Tester en local (sans compte)

```powershell
npm install
npx wrangler d1 execute dbm-hdv --local --file=schema.sql
Set-Content .dev.vars "GROUP_KEY=un-code-de-test"
npx wrangler dev                       # http://127.0.0.1:8787
```

## Limites

- Offre gratuite Cloudflare : 100 000 requêtes par jour. Chaque application ouverte fait environ 30 requêtes par heure : largement assez pour un groupe d'amis.
- Le serveur rejette les données incohérentes (prix négatifs ou démesurés, relevés de plus de 30 jours ou datés du futur), et un relevé plus ancien ne remplace jamais un plus récent.
- Un membre du groupe qui enverrait volontairement de faux prix pourrait fausser la base : ne partagez le code qu'avec des personnes de confiance.
