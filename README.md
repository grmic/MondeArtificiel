# Monde artificiel

Simulation d'une communauté humaine née d'un mythe fondateur. Application client-serveur : un serveur .NET 10 avec serveur HTTP intégré (Kestrel), et un client web ouvert dans le navigateur.

## Prérequis

- SDK .NET 10
- Node.js 22 ou plus (seulement pour modifier le client web)

## Lancer

```bash
cd src/Monde.Serveur
dotnet run
```

Puis ouvrir http://localhost:5080 : la page affiche « Connecté au serveur » et la carte du continent avec les 33 emplacements de départ.

En haut, la date du monde et les vitesses : pause, temps réel, puis l'accéléré jusqu'à « 5 min = 5 ans ». La carte s'assombrit la nuit. L'année compte 4 saisons de 90 jours, et la durée du jour varie selon la saison.

Le monde est sauvegardé toutes les 10 secondes et à l'arrêt du serveur (Ctrl+C), dans un fichier SQLite :

- macOS : `~/Library/Application Support/MondeArtificiel/mondes/monde.db`
- Windows : `%LOCALAPPDATA%\MondeArtificiel\mondes\monde.db`
- Linux : `~/.local/share/MondeArtificiel/mondes/monde.db`

Au redémarrage, le monde reprend à la date enregistrée, en pause. Pour repartir de zéro, supprime ce fichier (le serveur arrêté). Le dossier se change avec le réglage `Monde:Dossier`.

Sur la carte : molette pour zoomer, glisser pour se déplacer, double-clic pour recadrer, survol pour voir une tuile. Le bouton « Aperçu » montre le continent d'une autre graine sans changer le monde courant. La graine d'un nouveau monde se règle dans `src/Monde.Serveur/appsettings.json` (`Monde:Graine`). Une même graine donne toujours la même carte, sur toutes les plateformes.

## Tester

```bash
dotnet test
```

## Modifier le client web

```bash
cd client
npm install
npm run build   # compile dans src/Monde.Serveur/wwwroot
npm run dev     # ou : rechargement à chaud sur http://localhost:5173 (serveur .NET lancé à côté)
```

## Organisation

| Dossier | Rôle |
| --- | --- |
| `src/Monde.Simulation` | Moteur de simulation pur, sans web ni base. `Monde/GenerateurContinent.cs` crée la carte, `Temps/` gère le calendrier et les vitesses, `MondeSimule.cs` fait avancer le monde par pas de 10 minutes |
| `src/Monde.Serveur` | Kestrel, API, hub SignalR `/hub/monde`, API `/api/carte`, fichiers du client dans `wwwroot` |
| `src/Monde.Stockage` | Sauvegarde du monde dans SQLite (un fichier par monde) |
| `tests/Monde.Tests` | Tests xUnit (unitaires et d'intégration) |
| `client` | Client TypeScript construit avec Vite |

Le serveur n'écoute que sur `localhost:5080` (voir `src/Monde.Serveur/appsettings.json`).
