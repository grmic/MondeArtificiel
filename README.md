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

En haut, la date du monde et les vitesses préréglées : pause, temps réel, puis l'accéléré jusqu'à « 5 min = 5 ans ». Le panneau « Temps », à droite, donne le contrôle complet :

- **Vitesse libre** : un curseur du ralenti (×0,1) à ×10 000 000. Le monde va aussi vite que la machine le permet ; la vitesse réellement atteinte s'affiche à côté, en orange quand la machine ne suit pas.
- **Pas à pas** : met en pause, puis avance de 10 minutes, d'une heure ou d'un jour.
- **Aller jusqu'à** : demain matin, la prochaine saison, la prochaine année, la prochaine naissance, la prochaine mort ou une date précise. Le monde file à la vitesse maximale, puis se met en pause et l'annonce. Changer de vitesse ou « Arrêter » annule l'objectif.
- **Redémarrage** : une case choisit si, au redémarrage du serveur, le monde repart en pause (par défaut) ou à sa dernière vitesse.

Pour aller plus vite, lance le serveur en mode Release : `dotnet run -c Release`. La carte s'assombrit la nuit. L'année compte 4 saisons de 90 jours, et la durée du jour varie selon la saison.

Le monde est sauvegardé toutes les 10 secondes et à l'arrêt du serveur (Ctrl+C), dans un fichier SQLite :

- macOS : `~/Library/Application Support/MondeArtificiel/mondes/monde.db`
- Windows : `%LOCALAPPDATA%\MondeArtificiel\mondes\monde.db`
- Linux : `~/.local/share/MondeArtificiel/mondes/monde.db`

Au redémarrage, le monde reprend à la date enregistrée (en pause, sauf réglage contraire). Pour repartir de zéro, utilise le bouton « ↺ Réinitialiser » de la page : on choisit la même graine, celle du champ « Graine » ou une graine au hasard. L'ancien monde est copié dans `monde-avant-reinitialisation.db` à côté (seule la dernière copie est gardée). Le dossier se change avec le réglage `Monde:Dossier`.

Les 66 humains (33 couples) apparaissent sur la carte : point orange pour une femme, bleu pour un homme, croix grise pour un mort. Le compteur « Vivants » est en haut. Survoler un humain affiche son nom, son âge, ce qu'il fait et ses besoins (faim, soif, fatigue, froid, santé). Les noms sont tirés au hasard pour l'observateur : les humains n'ont pas encore de langage.

Le désir est un instinct qui apparaît à la puberté (les humains ne savent pas qu'il mène à une naissance). Ils s'accouplent avec leur compagnon ou avec un humain d'un autre couple. Rien n'est fixé à l'avance : chaque humain a sa propre biologie, héritée en partie de ses parents (âge de la puberté vers 16 ans, ménopause vers 49 ans, fertilité propre, 3 % de stérilité), et les valeurs de départ viennent d'études sur les chasseurs-cueilleurs, citées dans `Humains/Biologie.cs` et `Humains/Reproduction.cs`. La chance de concevoir dépend de l'âge, de la faim, de la santé et de l'allaitement (presque nulle la première année, l'allaitement dure jusque vers 3 ans). La grossesse dure environ 268 jours, plus ou moins selon chacune ; fausse couche, mort-né, jumeaux et mort de la mère ont des risques qui varient avec l'âge, le nombre d'enfants, la faim et la santé.

Chacun a aussi son propre attachement à ses enfants : fort chez la plupart, faible chez quelques-uns. Un parent attaché va rejoindre son enfant éloigné et partage nourriture et eau avec lui ; une mère peu attachée peut délaisser son bébé quand elle souffre. Les bébés restent dans les bras de leur mère jusqu'à 2 ans, puis les enfants suivent leurs parents jusqu'à 14 ans. Sur la carte, un enfant est un petit point et une femme enceinte a un contour blanc.

Chaque humain a une mémoire : il retient les points d'eau, les lieux où il a trouvé à manger et les abris où il a dormi (5 de chaque, oubliés au bout de 2 ans), et les humains qu'il côtoie. Quand rien n'est en vue, il retourne à un lieu dont il se souvient. Il pense aussi, en mots simples, à ce qu'il fait et pourquoi (« J'ai soif. Je me souviens d'une eau vers le nord. »). Cliquer sur un humain ouvre sa **fiche** : ce qu'il pense maintenant, sa famille (liens cliquables), ses besoins, son caractère, sa biologie, ses dernières pensées, sa mémoire et son histoire. Le bouton « Journal » ouvre le **journal du monde** : naissances, morts et leur cause, maladies, couples, grossesses, fausses couches, délaissements, filtrables par type et par nom, mis à jour en direct. Un clic sur une ligne ouvre la fiche. Les intuitions que l'Entité soufflera (étape 7) y seront marquées « ✦ soufflé par l'Entité ».

Sur la carte : molette pour zoomer, glisser pour se déplacer, double-clic pour recadrer, survol pour voir une tuile. Le bouton « Aperçu » montre le continent d'une autre graine sans changer le monde courant. La graine d'un nouveau monde se règle dans `src/Monde.Serveur/appsettings.json` (`Monde:Graine`). Une même graine donne toujours la même carte, sur toutes les plateformes.

## Tester

```bash
dotnet test
```

La suite complète prend environ trois minutes, car des tests simulent plusieurs années entières.

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
| `src/Monde.Simulation` | Moteur de simulation pur, sans web ni base. `Monde/GenerateurContinent.cs` crée la carte, `Temps/` gère le calendrier et les vitesses, `Humains/` contient la population, le climat, les ressources et les règles de comportement (`Comportement.cs`), `MondeSimule.cs` fait avancer le monde par pas de 10 minutes |
| `src/Monde.Serveur` | Kestrel, API, hub SignalR `/hub/monde`, API `/api/carte`, fichiers du client dans `wwwroot` |
| `src/Monde.Stockage` | Sauvegarde du monde dans SQLite (un fichier par monde) |
| `tests/Monde.Tests` | Tests xUnit (unitaires et d'intégration) |
| `client` | Client TypeScript construit avec Vite |

Le serveur n'écoute que sur `localhost:5080` (voir `src/Monde.Serveur/appsettings.json`).
