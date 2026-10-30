# Monde artificiel — Manuel : la base de données

Version 0.31.5 · 25 octobre 2026

## Où sont les fichiers

Tout le monde tient dans un seul fichier SQLite, `monde.db`, enregistré toutes les 10 secondes et à l'arrêt du serveur (Ctrl+C).

| Fichier ou dossier | Ce qu'il contient |
| --- | --- |
| `mondes/monde.db` | Le monde en cours : humains, terrain, journal, réglages |
| `mondes/monde.db-wal`, `monde.db-shm` | Fichiers de travail de SQLite, à laisser à côté de `monde.db` |
| `mondes/monde-avant-reinitialisation.db` | La copie de l'ancien monde faite à la dernière création d'un monde (une seule) |
| `mondes/sauvegardes/` | Les sauvegardes faites à la demande, et celles faites avant une restauration (`avant-restauration-…`) |
| `mondes/analyses/` | Les copies pour analyse, lisibles par un client SQL |

Le dossier `mondes` est à :

- **macOS** : `~/Library/Application Support/MondeArtificiel/mondes`
- **Linux** : `~/.local/share/MondeArtificiel/mondes`
- **Windows** : `%LOCALAPPDATA%\MondeArtificiel\mondes`

Sur Mac, dans le Terminal, mettez le chemin entre guillemets à cause de l'espace : `cd ~/"Library/Application Support/MondeArtificiel/mondes"`. Dans le Finder : Cmd+Maj+G puis collez le chemin. Le réglage `Monde:Dossier` de `appsettings.json` remplace ce dossier par un autre.

Ne touchez pas à `monde.db` pendant que le serveur tourne : arrêtez-le d'abord (Ctrl+C).

## Sauvegarder et restaurer

Une sauvegarde est une copie complète du monde à un instant donné ; tout se fait dans « ⚙ Configuration », rubrique Sauvegardes.

1. **Sauvegarder maintenant** copie le monde tel qu'il est, sans l'arrêter, dans `mondes/sauvegardes/`.
2. La liste montre chaque sauvegarde : date du monde, graine, humains vivants, taille en Mo.
3. **restaurer** remplace le monde en cours par la sauvegarde, en pause. Le monde remplacé est d'abord sauvegardé (`avant-restauration-…`) : on peut toujours y revenir. La sauvegarde restaurée n'est pas modifiée.
4. 🗑 met une sauvegarde à la corbeille de l'ordinateur, après confirmation ; on peut l'y reprendre tant qu'elle n'est pas vidée.

La copie gardée à la dernière création d'un monde (`monde-avant-reinitialisation.db`) apparaît aussi dans la liste et se restaure de la même façon.

Une sauvegarde pèse autant que le monde : un monde de 10 Go en donne une de 10 Go. Pour garder une sauvegarde ailleurs (disque externe), copiez le fichier depuis `sauvegardes/` ; pour la reprendre, remettez-le dans ce dossier.

## Créer un nouveau monde

« Créer ce monde » (Configuration, rubrique Nouveau monde) recommence à l'an 1 avec les réglages choisis ; toute l'histoire de l'ancien monde quitte `monde.db`.

La fenêtre de confirmation a une case **Garder une copie de l'ancien monde**, cochée à chaque ouverture.

| Case | Ce qui se passe |
| --- | --- |
| Cochée | L'ancien monde est enregistré puis déplacé dans `monde-avant-reinitialisation.db`, qui remplace la copie précédente. Il reste restaurable depuis la liste des sauvegardes. |
| Décochée | L'ancien monde est effacé sans copie et sa place est rendue au disque. La copie précédente, s'il y en a une, n'est pas touchée. |

Les réglages du serveur (langue, cœurs, clémence, conservation du journal…) passent au nouveau monde. Dans les deux cas, la création est quasi instantanée, même pour un gros monde : le fichier est déplacé ou supprimé, pas recopié (depuis la 0.31.5). Pour garder l'ancien monde plus longtemps qu'une création, faites d'abord une sauvegarde.

## Taille de la base

Le journal fait 99 % du poids de la base : sans réglage, un monde grossit sans fin. Mesuré pour environ 90 humains : 15 Mo par an avec la vie quotidienne, 6 Mo par an sans. Un monde de plusieurs centaines d'années peut donc dépasser les dizaines de Go.

La rubrique **Journal et taille de la base** de la Configuration montre la taille du monde, sa croissance mesurée sur la dernière année et le nombre de lignes du journal, en clair et compressées. La mesure se refait toutes les 10 minutes. Ses réglages :

| Réglage | Par défaut | Effet |
| --- | --- | --- |
| Conserver le journal | Illimitée | Limitée à N ans : les lignes plus anciennes sont effacées peu à peu, en arrière-plan. |
| Garder la vie quotidienne | Comme le reste | Résumés des journées, pensées, partages : souvent la plus grosse part. On peut les garder moins longtemps que le reste. |
| Effacer aussi les faits marquants | Décochée | Décochée, les faits marquants sont gardés quelle que soit la durée. |
| Effacer aussi le journal des humains suivis | Décochée | Décochée, toute ligne où un humain suivi (★) agit ou est concerné est gardée. |
| Compresser le journal ancien | Décochée | Range le journal plus vieux que N ans (1 par défaut) par paquets compressés, sans perte, environ dix fois moins de place. Le jeu le lit toujours en entier. |

Ce qui est effacé ne revient pas : une confirmation le rappelle, et faire d'abord une sauvegarde est conseillé. Suivre quelqu'un plus tard ne fait pas revenir ses lignes déjà effacées.

Le fichier rend la place libérée au démarrage suivant du serveur, quand plus de la moitié en est vide. Pour un monde que l'on veut faire tourner longtemps, la combinaison la plus efficace est de cocher la compression et de garder la vie quotidienne quelques années seulement.

## Copie pour analyse

Pour étudier le monde avec un client SQL externe (DB Browser for SQLite, DBeaver, TablePlus…), utilisez une copie pour analyse plutôt que `monde.db`. Le journal compressé n'est pas lisible directement par un client SQL, et ouvrir le fichier du monde pendant que le serveur tourne peut le gêner.

1. Dans Journal et taille de la base, choisissez au besoin une période (de l'an … à l'an …) ; vide = tout le journal.
2. Cliquez **Créer une copie pour analyse** : quelques minutes pour un gros monde, pendant lesquelles le monde continue de tourner.
3. Le fichier arrive dans `mondes/analyses/`, nommé d'après l'année du monde et l'heure (`analyse-an893-20261025-174500.db`).

Dans la copie, tout le journal est en clair, archives comprises, et la table `journal` a une colonne `texte_redige` qui contient la phrase telle que le jeu l'affiche. Depuis la 0.32.5, les tables `mots` (les mots de chaque humain) et `langue_histoire` (l'histoire des mots de chaque communauté) y ont une colonne `sens`, le sens du mot en clair, dans la langue du jeu. La vue `lexique` donne les mots en usage dans chaque communauté, sans les mots oubliés : `SELECT nom_communaute, sens, mot, sorte, depuis_an, nom_auteur FROM lexique WHERE communaute = 12 ORDER BY sens;`. Les phrases que disent les humains ne sont pas enregistrées : seuls les mots le sont. Ces copies ne servent qu'à lire : le jeu ne les restaure pas, et vous pouvez les effacer quand vous voulez.

## Mises à jour et problèmes

Une nouvelle version met la base à jour toute seule au démarrage, sans perte : la 0.31.5 passe du schéma 38 au 39 (le peuple de chaque humain, son voyage en mer, les premières avec les terres d'outre-mer). Une base d'une version plus récente que le jeu est refusée plutôt qu'abîmée. La 0.32.6 passe au schéma 43 : la table `huttes` gagne la colonne `enduite` (quand la hutte a été enduite de chaux, 0 si jamais).

| Symptôme | Que faire |
| --- | --- |
| La base dépasse des dizaines de Go | Limiter la conservation ou la vie quotidienne, cocher la compression ; la place revient au redémarrage suivant. |
| « Création du monde… » ne finit pas (avant 0.31.5) | Le monde était recopié en entier ; corrigé en 0.31.5. Sinon : arrêter le serveur, déplacer `monde.db`, relancer (un monde neuf est créé). |
| `monde.db` effacé à la main | Arrêter puis relancer le serveur : il crée un monde neuf au démarrage. Effacer aussi `monde.db-wal` et `monde.db-shm`. |
| Une sauvegarde ne se restaure pas | Elle est illisible ou d'une version plus récente : rien n'est changé, le monde en cours reste. |
| Le disque est plein | Mettre à la corbeille les vieilles sauvegardes et effacer `analyses/` et `monde-avant-reinitialisation.db` si on n'en a plus besoin. |

Toute manipulation à la main des fichiers se fait serveur arrêté : `monde.db` et ses deux fichiers `-wal` et `-shm` vont toujours ensemble.
