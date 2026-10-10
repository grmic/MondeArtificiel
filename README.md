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

Chaque humain a son **caractère** et son **intelligence**, innés et transmis en partie à ses enfants (`Humains/Psyche.cs`) : les cinq grands traits du modèle Big Five (ouverture, conscience, extraversion, agréabilité, névrosisme), la transcendance de soi (spirituel ou terre à terre, d'après Cloninger), l'attachement à ses enfants et l'endurance. L'intelligence suit l'échelle du QI (moyenne 100, écart-type 15) : une intelligence générale et des aptitudes verbale, spatiale, manuelle, de mémoire et artistique ; quelques rares humains ont un talent artistique exceptionnel. Le charisme se déduit du caractère ; la figure centrale d'un groupe n'est pas désignée, elle émerge.

La **mémoire** est propre à chacun : un humain retient les points d'eau, les lieux où il a mangé et les abris où il a dormi (de 2 à 12 de chaque selon son aptitude), et les oublie selon la courbe d'Ebbinghaus : vite s'il a peu de mémoire, lentement s'il en a beaucoup, et de moins en moins à chaque retour. Un abri où il revient chaque saison peut ne jamais être oublié. Quand rien n'est en vue, il retourne à un lieu dont il se souvient.

Les humains tissent des **liens** (familiarité, affection) avec ceux qu'ils côtoient : on s'attache davantage à qui nous ressemble et quand on est bienveillant ; les querelleurs et les anxieux se heurtent, et la foule crée des frictions. Des amitiés et des hostilités naissent et s'éteignent ; on partage sa nourriture avec ceux qu'on aime, on se serre contre eux pour avoir chaud, on s'éloigne de ceux qu'on ne supporte plus. Leur **humeur** varie (faim, froid, compagnie, naissances) ; la mort d'un proche endeuille sa famille, ses amis et sa communauté, plus ou moins longtemps selon la sensibilité de chacun. Une tristesse qui dure peut tourner à la dépression (l'humain se néglige, délaisse les siens) et, rarement, au suicide.

Les **communautés** ne sont pas décrétées : chaque semaine, le monde constate les groupes d'amis qui vivent ensemble (au moins 5 humains dont 3 adultes). Le bouton « Communautés » les liste avec leurs membres, leur date de naissance, leur figure centrale et l'endroit où elles vivent ; en choisir une entoure ses membres sur la carte.

Cliquer sur un humain ouvre sa **fiche** : pensée du moment, humeur et chagrin, communauté, famille, besoins, caractère, intelligence, biologie, liens, dernières pensées, lieux dont il se souvient et histoire. Le bouton « Journal » ouvre le **journal du monde** : naissances, morts, maladies, couples, grossesses, amitiés et hostilités, deuils, communautés, partages, pensées, résumé quotidien des gestes de chacun, lieux oubliés. Tout est enregistré ; on filtre par type et par nom. Les types très fréquents (pensées, journées, oublis, partages, accouplements) ne s'affichent pas en direct : le bouton ↻ relit le journal. Les intuitions que l'Entité soufflera (étape 7) y seront marquées « ✦ soufflé par l'Entité ».

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
