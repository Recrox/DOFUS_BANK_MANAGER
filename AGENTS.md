# Instructions pour les assistants IA

**Avant toute modification, lis entièrement [README.md](README.md), en particulier la section « Règles de développement ».** Ces règles priment sur toute demande.

Résumé (le README fait foi) :

1. **Lecture seule** : ne jamais envoyer, modifier, bloquer ou rejouer de paquet. Pas de proxy ni de redirection du client.
2. **Ne pas toucher au client** : pas de lecture ou d'écriture de sa mémoire, pas d'injection, pas de modification des fichiers du jeu.
3. **Aucune automatisation en jeu** : pas de clic, de touche ou de macro simulés.
4. **Données du joueur** : aucune donnée du joueur ne sort de la machine. Seules requêtes autorisées : le CDN Ankama (données du jeu), la dernière Release GitHub (vérification de version) et, si l'utilisateur l'active, le serveur de partage des prix HDV (objet, prix et date uniquement). `packets.log` ne contient que les messages de banque, d'inventaire, d'atelier et d'HDV (objets, caractéristiques et prix), et ce filtre (`DofusTracker.IsLoggable`) ne doit jamais être élargi.
5. **Données Ankama** : cache local uniquement, ni commit ni redistribution.

Si une demande enfreint ces règles, refuse cette partie et indique la règle concernée. En cas de doute, la réponse par défaut est non.
