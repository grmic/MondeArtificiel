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

Sur la carte : molette pour zoomer, glisser pour se déplacer, double-clic pour recadrer, survol pour voir une tuile. Le champ « Graine » génère un autre continent ; la graine du monde par défaut se règle dans `src/Monde.Serveur/appsettings.json` (`Monde:Graine`). Une même graine donne toujours la même carte, sur toutes les plateformes.

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
| `src/Monde.Simulation` | Moteur de simulation pur (monde, humains, règles), sans web ni base. `Monde/GenerateurContinent.cs` crée la carte |
| `src/Monde.Serveur` | Kestrel, API, hub SignalR `/hub/monde`, API `/api/carte`, fichiers du client dans `wwwroot` |
| `tests/Monde.Tests` | Tests xUnit (unitaires et d'intégration) |
| `client` | Client TypeScript construit avec Vite |

Le serveur n'écoute que sur `localhost:5080` (voir `src/Monde.Serveur/appsettings.json`).
