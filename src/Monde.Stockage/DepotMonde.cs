using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monde.Simulation;
using Monde.Simulation.Humains;
using Monde.Simulation.Monde;
using Monde.Simulation.Nature;

namespace Monde.Stockage;

/// <summary>
/// Sauvegarde d'un monde dans un fichier SQLite. Un monde = un fichier,
/// ce qui rendra les snapshots et les forks simples (copie du fichier).
/// Toutes les requêtes sont paramétrées ; une sauvegarde complète tient dans une transaction.
/// </summary>
public sealed partial class DepotMonde
{
    /// <summary>Version du schéma, stockée dans PRAGMA user_version.</summary>
    public const int VersionSchema = 38;

    private readonly string _connexion;

    public DepotMonde(string cheminFichier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cheminFichier);
        CheminFichier = Path.GetFullPath(cheminFichier);
        Directory.CreateDirectory(Path.GetDirectoryName(CheminFichier)!);
        _connexion = new SqliteConnectionStringBuilder
        {
            DataSource = CheminFichier,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false, // le fichier est libéré dès la fermeture (copie, fork)
        }.ToString();
        Initialiser();
    }

    public string CheminFichier { get; }

    private SqliteConnection Ouvrir()
    {
        var connexion = new SqliteConnection(_connexion);
        connexion.Open();
        return connexion;
    }

    private static void Executer(SqliteConnection connexion, SqliteTransaction? transaction, string sql)
    {
        using var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
        commande.CommandText = sql;
        commande.ExecuteNonQuery();
    }

    private void Initialiser()
    {
        using var connexion = Ouvrir();
        Executer(connexion, null, "PRAGMA journal_mode = WAL;");

        using var version = connexion.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        var actuelle = Convert.ToInt32(version.ExecuteScalar());
        if (actuelle > VersionSchema)
        {
            throw new InvalidOperationException(
                $"Le fichier {CheminFichier} vient d'une version plus récente de l'application (schéma {actuelle}).");
        }
        if (actuelle == VersionSchema) return;

        using var transaction = connexion.BeginTransaction();
        if (actuelle < 1)
        {
            Executer(connexion, transaction, """
                CREATE TABLE etat (
                    id          INTEGER PRIMARY KEY CHECK (id = 1),
                    graine      INTEGER NOT NULL,
                    secondes    INTEGER NOT NULL CHECK (secondes >= 0),
                    pas         INTEGER NOT NULL CHECK (pas >= 0),
                    vitesse     INTEGER NOT NULL,
                    modifie_le  TEXT    NOT NULL
                );
                """);
        }
        if (actuelle < 2)
        {
            // Version 2 : humains, ressources des tuiles et état du générateur aléatoire.
            Executer(connexion, transaction, """
                ALTER TABLE etat ADD COLUMN alea0 INTEGER;
                ALTER TABLE etat ADD COLUMN alea1 INTEGER;
                ALTER TABLE etat ADD COLUMN alea2 INTEGER;
                ALTER TABLE etat ADD COLUMN alea3 INTEGER;
                CREATE TABLE ressources (
                    id      INTEGER PRIMARY KEY CHECK (id = 1),
                    gibier  BLOB NOT NULL,
                    plantes BLOB NOT NULL
                );
                CREATE TABLE humains (
                    id            INTEGER PRIMARY KEY,
                    nom           TEXT    NOT NULL,
                    sexe          INTEGER NOT NULL,
                    partenaire    INTEGER NOT NULL,
                    emplacement   INTEGER NOT NULL,
                    naissance     INTEGER NOT NULL,
                    curiosite     REAL    NOT NULL,
                    prudence      REAL    NOT NULL,
                    sociabilite   REAL    NOT NULL,
                    endurance     REAL    NOT NULL,
                    adresse       REAL    NOT NULL,
                    x             INTEGER NOT NULL,
                    y             INTEGER NOT NULL,
                    faim          REAL    NOT NULL,
                    soif          REAL    NOT NULL,
                    fatigue       REAL    NOT NULL,
                    froid         REAL    NOT NULL,
                    sante         REAL    NOT NULL,
                    activite      INTEGER NOT NULL,
                    cible_x       INTEGER NOT NULL,
                    cible_y       INTEGER NOT NULL,
                    detour        INTEGER NOT NULL,
                    malade_jusqua INTEGER NOT NULL,
                    mort_le       INTEGER,
                    cause_mort    INTEGER NOT NULL
                );
                """);
        }
        if (actuelle < 3)
        {
            // Version 3 : filiation, désir et grossesse.
            Executer(connexion, transaction, """
                ALTER TABLE humains ADD COLUMN mere INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN pere INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN desir REAL NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN enceinte_depuis INTEGER;
                ALTER TABLE humains ADD COLUMN pere_enfant INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN dernier_accouchement INTEGER;
                """);
        }
        if (actuelle < 4)
        {
            // Version 4 : attachement aux enfants, biologie propre à chacun, terme de la grossesse.
            // Valeurs nulles pour les humains déjà enregistrés : des valeurs par défaut sont prises au chargement.
            Executer(connexion, transaction, """
                ALTER TABLE humains ADD COLUMN attachement REAL;
                ALTER TABLE humains ADD COLUMN puberte REAL;
                ALTER TABLE humains ADD COLUMN fin_fertilite REAL;
                ALTER TABLE humains ADD COLUMN fertilite REAL;
                ALTER TABLE humains ADD COLUMN terme_grossesse INTEGER;
                """);
        }
        if (actuelle < 5)
        {
            // Version 5 : la vitesse devient libre (facteur au lieu d'un niveau préréglé) ; réglages de l'application.
            // Une base neuve n'a encore aucune ligne dans « etat » : la conversion ne touche alors rien.
            Executer(connexion, transaction, """
                UPDATE etat SET vitesse = CASE vitesse
                    WHEN 1 THEN 1 WHEN 2 THEN 60 WHEN 3 THEN 1440 WHEN 4 THEN 129600 WHEN 5 THEN 518400 ELSE 0 END
                WHERE typeof(vitesse) = 'integer';
                CREATE TABLE IF NOT EXISTS reglages (
                    cle    TEXT PRIMARY KEY,
                    valeur TEXT NOT NULL
                );
                """);
        }
        if (actuelle < 6)
        {
            // Version 6 : journal des événements, mémoire des humains (lieux, proches) et pensées.
            Executer(connexion, transaction, """
                CREATE TABLE IF NOT EXISTS journal (
                    id      INTEGER PRIMARY KEY,
                    secondes INTEGER NOT NULL,
                    type    INTEGER NOT NULL,
                    humain  INTEGER NOT NULL,
                    autre   INTEGER NOT NULL,
                    texte   TEXT    NOT NULL,
                    origine INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS journal_humain ON journal (humain);
                CREATE INDEX IF NOT EXISTS journal_autre ON journal (autre);
                CREATE TABLE IF NOT EXISTS memoire_lieux (
                    humain INTEGER NOT NULL,
                    rang   INTEGER NOT NULL,
                    type   INTEGER NOT NULL,
                    x      INTEGER NOT NULL,
                    y      INTEGER NOT NULL,
                    vu_le  INTEGER NOT NULL,
                    PRIMARY KEY (humain, rang)
                );
                CREATE TABLE IF NOT EXISTS memoire_proches (
                    humain     INTEGER NOT NULL,
                    rang       INTEGER NOT NULL,
                    autre      INTEGER NOT NULL,
                    rencontres INTEGER NOT NULL,
                    derniere   INTEGER NOT NULL,
                    PRIMARY KEY (humain, rang)
                );
                CREATE TABLE IF NOT EXISTS pensees (
                    humain   INTEGER NOT NULL,
                    rang     INTEGER NOT NULL,
                    secondes INTEGER NOT NULL,
                    texte    TEXT    NOT NULL,
                    PRIMARY KEY (humain, rang)
                );
                """);
        }
        if (actuelle < 7)
        {
            // Version 7 : caractère complet (Big Five, transcendance), intelligence, humeur et deuil, liens,
            // communautés, mémoire des lieux renforcée par les rappels ; index du journal adaptés au filtrage.
            Executer(connexion, transaction, """
                ALTER TABLE humains ADD COLUMN agreabilite REAL;
                ALTER TABLE humains ADD COLUMN nevrosisme REAL;
                ALTER TABLE humains ADD COLUMN transcendance REAL;
                ALTER TABLE humains ADD COLUMN qi REAL;
                ALTER TABLE humains ADD COLUMN qi_verbal REAL;
                ALTER TABLE humains ADD COLUMN qi_spatial REAL;
                ALTER TABLE humains ADD COLUMN qi_manuel REAL;
                ALTER TABLE humains ADD COLUMN qi_memoire REAL;
                ALTER TABLE humains ADD COLUMN qi_art REAL;
                ALTER TABLE humains ADD COLUMN humeur REAL NOT NULL DEFAULT 50;
                ALTER TABLE humains ADD COLUMN chagrin REAL NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN deuil_de INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN jours_sombres INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN deprime_depuis INTEGER;
                ALTER TABLE humains ADD COLUMN communaute INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN inconfort REAL NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN compagnie INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE memoire_lieux ADD COLUMN rappels INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE memoire_lieux ADD COLUMN decu_le INTEGER;
                CREATE TABLE IF NOT EXISTS liens (
                    humain      INTEGER NOT NULL,
                    rang        INTEGER NOT NULL,
                    autre       INTEGER NOT NULL,
                    familiarite REAL    NOT NULL,
                    affection   REAL    NOT NULL,
                    derniere    INTEGER NOT NULL,
                    amis        INTEGER NOT NULL,
                    rivaux      INTEGER NOT NULL,
                    PRIMARY KEY (humain, rang)
                );
                INSERT OR IGNORE INTO liens (humain, rang, autre, familiarite, affection, derniere, amis, rivaux)
                    SELECT humain, rang, autre, min(100, rencontres), 0, derniere, 0, 0 FROM memoire_proches;
                DROP TABLE memoire_proches;
                CREATE TABLE IF NOT EXISTS communautes (
                    id           INTEGER PRIMARY KEY,
                    nom          TEXT    NOT NULL,
                    fondee_le    INTEGER NOT NULL,
                    dispersee_le INTEGER,
                    figure       INTEGER NOT NULL
                );
                DROP INDEX IF EXISTS journal_humain;
                DROP INDEX IF EXISTS journal_autre;
                CREATE INDEX IF NOT EXISTS journal_humain_id ON journal (humain, id);
                CREATE INDEX IF NOT EXISTS journal_autre_id ON journal (autre, id);
                CREATE INDEX IF NOT EXISTS journal_type_id ON journal (type, id);
                """);
        }
        if (actuelle < 8)
        {
            // Version 8 : réserves du corps et métabolisme propre, pluies des régions, territoire et disette
            // des communautés, relations entre communautés.
            Executer(connexion, transaction, """
                ALTER TABLE humains ADD COLUMN reserves REAL NOT NULL DEFAULT 50;
                ALTER TABLE humains ADD COLUMN metabolisme REAL;
                ALTER TABLE communautes ADD COLUMN centre_x INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN centre_y INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN rayon INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN abondance REAL NOT NULL DEFAULT 1;
                ALTER TABLE communautes ADD COLUMN disette INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN destination_x INTEGER NOT NULL DEFAULT -1;
                ALTER TABLE communautes ADD COLUMN destination_y INTEGER NOT NULL DEFAULT -1;
                CREATE TABLE IF NOT EXISTS climat (
                    id     INTEGER PRIMARY KEY CHECK (id = 1),
                    pluies BLOB NOT NULL
                );
                CREATE TABLE IF NOT EXISTS relations_communautes (
                    a     INTEGER NOT NULL,
                    b     INTEGER NOT NULL,
                    etat  INTEGER NOT NULL,
                    PRIMARY KEY (a, b)
                );
                """);
        }
        if (actuelle < 9)
        {
            // Version 9 : causes des événements, raisons de l'humeur, colère et rancunes, usages des communautés,
            // offenses entre communautés.
            Executer(connexion, transaction, """
                ALTER TABLE journal ADD COLUMN cause INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN faim_soif_jour REAL NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN froid_jour REAL NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN jours_seul INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN colere REAL NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN deuil_evenement INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN depression_evenement INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN mort_evenement INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN meurtrier INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN tolerance REAL NOT NULL DEFAULT 0.5;
                ALTER TABLE communautes ADD COLUMN usage INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN derniere_affaire INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN disette_evenement INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE IF NOT EXISTS griefs (
                    humain    INTEGER NOT NULL,
                    rang      INTEGER NOT NULL,
                    contre    INTEGER NOT NULL,
                    motif     INTEGER NOT NULL,
                    intensite REAL    NOT NULL,
                    initiale  REAL    NOT NULL,
                    evenement INTEGER NOT NULL,
                    victime   INTEGER NOT NULL,
                    PRIMARY KEY (humain, rang)
                );
                CREATE TABLE IF NOT EXISTS raisons (
                    humain INTEGER NOT NULL,
                    rang   INTEGER NOT NULL,
                    type   INTEGER NOT NULL,
                    autre  INTEGER NOT NULL,
                    poids  REAL    NOT NULL,
                    PRIMARY KEY (humain, rang)
                );
                CREATE TABLE IF NOT EXISTS offenses_communautes (
                    a         INTEGER NOT NULL,
                    b         INTEGER NOT NULL,
                    evenement INTEGER NOT NULL,
                    PRIMARY KEY (a, b)
                );
                """);
        }
        if (actuelle < 10)
        {
            // Version 10 : identité et histoire des communautés (noms, mots, fusions, terres parcourues, archives de saison),
            // communautés concernées par chaque événement, événement à l'origine d'une relation entre communautés.
            Executer(connexion, transaction, """
                ALTER TABLE journal ADD COLUMN communaute INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE journal ADD COLUMN autre_communaute INTEGER NOT NULL DEFAULT 0;
                CREATE INDEX IF NOT EXISTS journal_communaute_id ON journal (communaute, id);
                CREATE INDEX IF NOT EXISTS journal_autre_communaute_id ON journal (autre_communaute, id);
                ALTER TABLE communautes ADD COLUMN nom_propre TEXT;
                ALTER TABLE communautes ADD COLUMN sens TEXT;
                ALTER TABLE communautes ADD COLUMN sens_cle TEXT;
                ALTER TABLE communautes ADD COLUMN eponyme INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN lexique TEXT NOT NULL DEFAULT '';
                ALTER TABLE communautes ADD COLUMN origine INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN fusion_vers INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN fusion_decidee_le INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN fusion_evenement INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN fusionnee_dans INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN accueil_le INTEGER;
                ALTER TABLE communautes ADD COLUMN migrations INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN disettes_surmontees INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN installation_evenement INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN territoire BLOB;
                ALTER TABLE relations_communautes ADD COLUMN evenement INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE IF NOT EXISTS noms_communautes (
                    communaute INTEGER NOT NULL,
                    rang       INTEGER NOT NULL,
                    nom        TEXT    NOT NULL,
                    sens       TEXT,
                    depuis     INTEGER NOT NULL,
                    evenement  INTEGER NOT NULL,
                    PRIMARY KEY (communaute, rang)
                );
                INSERT OR IGNORE INTO noms_communautes (communaute, rang, nom, sens, depuis, evenement)
                    SELECT id, 0, nom, NULL, fondee_le, 0 FROM communautes;
                CREATE TABLE IF NOT EXISTS archives_communautes (
                    communaute INTEGER NOT NULL,
                    secondes   INTEGER NOT NULL,
                    membres    INTEGER NOT NULL,
                    adultes    INTEGER NOT NULL,
                    cases      BLOB    NOT NULL,
                    PRIMARY KEY (communaute, secondes)
                );
                CREATE INDEX IF NOT EXISTS archives_secondes ON archives_communautes (secondes);
                """);
            RattacherJournalAuxCommunautes(connexion, transaction);
        }
        if (actuelle < 11)
        {
            // Version 11 : catastrophes naturelles, maladies, peur et infections, dégâts sur la nature.
            MigrerVersion11(connexion, transaction);
        }
        if (actuelle < 12)
        {
            // Version 12 : distances réelles (allure de marche de chacun, chemin fait vers la case suivante).
            Executer(connexion, transaction, """
                ALTER TABLE humains ADD COLUMN marche REAL;
                ALTER TABLE humains ADD COLUMN progression REAL NOT NULL DEFAULT 0;
                ALTER TABLE humains ADD COLUMN bebe_porte_le INTEGER NOT NULL DEFAULT -1;
                """);
        }
        if (actuelle < 13)
        {
            // Version 13 : réglages du monde (couples de départ, taille de la carte, continents).
            // Vides pour un monde plus ancien : il garde les réglages d'origine.
            Executer(connexion, transaction, """
                ALTER TABLE etat ADD COLUMN couples INTEGER;
                ALTER TABLE etat ADD COLUMN largeur INTEGER;
                ALTER TABLE etat ADD COLUMN hauteur INTEGER;
                ALTER TABLE etat ADD COLUMN continents INTEGER;
                """);
        }
        if (actuelle < 14)
        {
            // Version 14 : guerres et alliances (raids, pactes, soumission, paix) ; continent de départ des couples.
            Executer(connexion, transaction, """
                ALTER TABLE etat ADD COLUMN placement INTEGER NOT NULL DEFAULT 1;
                ALTER TABLE communautes ADD COLUMN bellicisme REAL NOT NULL DEFAULT -1;
                ALTER TABLE communautes ADD COLUMN raid TEXT;
                ALTER TABLE communautes ADD COLUMN dernier_raid_le INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN soumise_a INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN soumise_le INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN soumission_evenement INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN paix_evenement INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN heritiere INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE relations_communautes ADD COLUMN pacte INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE IF NOT EXISTS guerres (
                    id INTEGER PRIMARY KEY,
                    a INTEGER NOT NULL,
                    b INTEGER NOT NULL,
                    debut INTEGER NOT NULL,
                    fin INTEGER,
                    morts_a INTEGER NOT NULL,
                    morts_b INTEGER NOT NULL,
                    raids INTEGER NOT NULL,
                    issue INTEGER NOT NULL,
                    dernier_sang INTEGER NOT NULL,
                    evenement INTEGER NOT NULL,
                    fin_evenement INTEGER NOT NULL,
                    vainqueur INTEGER NOT NULL
                );
                """);
        }
        if (actuelle < 15)
        {
            // Version 15 : jardin secret de chacun, guerres en préparation, événements significatifs.
            Executer(connexion, transaction, """
                CREATE TABLE IF NOT EXISTS jardin (
                    humain INTEGER NOT NULL,
                    rang INTEGER NOT NULL,
                    secondes INTEGER NOT NULL,
                    type INTEGER NOT NULL,
                    sujet INTEGER NOT NULL,
                    texte TEXT NOT NULL,
                    intensite REAL NOT NULL,
                    cause INTEGER NOT NULL,
                    cle TEXT NOT NULL,
                    PRIMARY KEY (humain, rang)
                );
                ALTER TABLE communautes ADD COLUMN projet TEXT;
                ALTER TABLE journal ADD COLUMN significatif INTEGER NOT NULL DEFAULT 0;
                CREATE INDEX IF NOT EXISTS journal_significatif_id ON journal (significatif, id);
                """);
        }
        if (actuelle < 16)
        {
            // Version 16 : apparence, orientation et maladie de chacun, exclusions, mœurs et idéal de beauté des communautés.
            Executer(connexion, transaction, """
                ALTER TABLE humains ADD COLUMN symetrie REAL;
                ALTER TABLE humains ADD COLUMN harmonie REAL;
                ALTER TABLE humains ADD COLUMN taille REAL;
                ALTER TABLE humains ADD COLUMN teint REAL;
                ALTER TABLE humains ADD COLUMN allure REAL;
                ALTER TABLE humains ADD COLUMN vers_hommes REAL;
                ALTER TABLE humains ADD COLUMN vers_femmes REAL;
                ALTER TABLE humains ADD COLUMN lucidite REAL;
                ALTER TABLE humains ADD COLUMN maladie INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN ideal_taille REAL;
                ALTER TABLE communautes ADD COLUMN ideal_teint REAL;
                ALTER TABLE communautes ADD COLUMN ideal_corpulence REAL;
                ALTER TABLE communautes ADD COLUMN ideal_note INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN tolerance_moeurs REAL;
                ALTER TABLE communautes ADD COLUMN usage_moeurs INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE communautes ADD COLUMN affaire_moeurs INTEGER NOT NULL DEFAULT 0;
                CREATE TABLE IF NOT EXISTS exclusions (
                    humain INTEGER NOT NULL,
                    rang INTEGER NOT NULL,
                    type INTEGER NOT NULL,
                    de INTEGER NOT NULL,
                    le INTEGER NOT NULL,
                    jusqua INTEGER NOT NULL,
                    evenement INTEGER NOT NULL,
                    raison TEXT NOT NULL,
                    levee INTEGER NOT NULL,
                    PRIMARY KEY (humain, rang)
                );
                """);
        }
        if (actuelle < 17)
        {
            // Version 17 : suites des catastrophes (jusqu'à quand la terre dévastée tue encore, et combien).
            Executer(connexion, transaction, """
                ALTER TABLE catastrophes ADD COLUMN suites_jusqua INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE catastrophes ADD COLUMN morts_suites INTEGER NOT NULL DEFAULT 0;
                """);
        }
        if (actuelle < 18)
        {
            // Version 18 : honnêteté, empathies, compassion, psychopathie, attachement adulte et fondements moraux.
            // Vides pour les humains déjà nés : ils sont tirés à la lecture, d'après leurs autres traits (voir LirePersonnalite).
            Executer(connexion, transaction, """
                ALTER TABLE humains ADD COLUMN honnetete REAL;
                ALTER TABLE humains ADD COLUMN empathie_emo REAL;
                ALTER TABLE humains ADD COLUMN empathie_cog REAL;
                ALTER TABLE humains ADD COLUMN compassion REAL;
                ALTER TABLE humains ADD COLUMN psychopathie REAL;
                ALTER TABLE humains ADD COLUMN anxiete_aff REAL;
                ALTER TABLE humains ADD COLUMN evitement_aff REAL;
                ALTER TABLE humains ADD COLUMN soin REAL;
                ALTER TABLE humains ADD COLUMN equite REAL;
                ALTER TABLE humains ADD COLUMN loyaute REAL;
                ALTER TABLE humains ADD COLUMN autorite REAL;
                ALTER TABLE humains ADD COLUMN purete REAL;
                """);
        }
        if (actuelle < 19) MigrerVersion19(connexion, transaction);
        if (actuelle < 20) MigrerVersion20(connexion, transaction);
        if (actuelle < 21) MigrerVersion21(connexion, transaction);
        if (actuelle < 22)
        {
            // Version 22 : femmes enlevées lors des raids (colonnes ajoutées si elles manquent).
            using var colonnes = connexion.CreateCommand();
            colonnes.Transaction = transaction;
            colonnes.CommandText = "SELECT name FROM pragma_table_info('humains');";
            var existantes = new HashSet<string>();
            using (var l = colonnes.ExecuteReader()) while (l.Read()) existantes.Add(l.GetString(0));
            if (!existantes.Contains("captif_de")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN captif_de INTEGER NOT NULL DEFAULT 0;");
            if (!existantes.Contains("captif_le")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN captif_le INTEGER NOT NULL DEFAULT 0;");
        }
        if (actuelle < 23)
        {
            // Version 23 : karma (sur l'humain et sur l'acte qui le change) et croyance en l'invisible. Une croyance absente
            // (monde plus ancien) est tirée du numéro de l'humain au chargement.
            var humains = Colonnes(connexion, transaction, "humains");
            if (!humains.Contains("karma")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN karma REAL NOT NULL DEFAULT 0;");
            if (!humains.Contains("croyance")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN croyance REAL;");
            if (!humains.Contains("croyance_signe")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN croyance_signe INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("croyance_notee")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN croyance_notee REAL;");
            var journal = Colonnes(connexion, transaction, "journal");
            if (!journal.Contains("karma")) Executer(connexion, transaction, "ALTER TABLE journal ADD COLUMN karma REAL NOT NULL DEFAULT 0;");
            if (!journal.Contains("karma_autre")) Executer(connexion, transaction, "ALTER TABLE journal ADD COLUMN karma_autre REAL NOT NULL DEFAULT 0;");
            if (!Colonnes(connexion, transaction, "liens").Contains("bienfait"))
                Executer(connexion, transaction, "ALTER TABLE liens ADD COLUMN bienfait INTEGER NOT NULL DEFAULT 0;");
        }
        if (actuelle < 24)
        {
            // Version 24 : médecine (soignants, plaies, malédictions).
            var humains = Colonnes(connexion, transaction, "humains");
            if (!humains.Contains("soignes")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN soignes INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("gueris")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN gueris INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("perdus")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN perdus INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("accouchements_aides")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN accouchements_aides INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("guerisseur_depuis")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN guerisseur_depuis INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("chaman_depuis")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN chaman_depuis INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("transe_le")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN transe_le INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("charlatan_depuis")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN charlatan_depuis INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("demasque_le")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN demasque_le INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("soigneur")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN soigneur INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("soigneur_le")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN soigneur_le INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("plaie_le")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN plaie_le INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("plaie_soin")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN plaie_soin REAL NOT NULL DEFAULT 0;");
            if (!humains.Contains("maudit_le")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN maudit_le INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("maudit_par")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN maudit_par INTEGER NOT NULL DEFAULT 0;");
            if (!humains.Contains("maudit_force")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN maudit_force REAL NOT NULL DEFAULT 0;");
            if (!humains.Contains("maudit_evenement")) Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN maudit_evenement INTEGER NOT NULL DEFAULT 0;");
        }
        // Version 25 : arts (images peintes sur la roche).
        if (actuelle < 25) MigrerVersion25(connexion, transaction);
        if (actuelle < 26) MigrerVersion26(connexion, transaction);
        // Version 27 : dernière communauté de ceux qui n'en ont plus (pour qu'elle renaisse s'ils se retrouvent).
        if (actuelle < 27 && !Colonnes(connexion, transaction, "humains").Contains("ancienne_communaute"))
            Executer(connexion, transaction, "ALTER TABLE humains ADD COLUMN ancienne_communaute INTEGER NOT NULL DEFAULT 0;");
        // Version 28 : prières, prophètes et religions.
        if (actuelle < 28) MigrerVersion28(connexion, transaction);
        if (actuelle < 29) MigrerVersion29(connexion, transaction);
        // Version 30 : gestes de l'Entité, regard sur l'invisible.
        if (actuelle < 30) MigrerVersion30(connexion, transaction);
        // Version 31 : prières collectives et prières refusées.
        if (actuelle < 31)
        {
            var prieres = Colonnes(connexion, transaction, "prieres");
            if (!prieres.Contains("communaute")) Executer(connexion, transaction, "ALTER TABLE prieres ADD COLUMN communaute INTEGER NOT NULL DEFAULT 0;");
            if (!prieres.Contains("refus")) Executer(connexion, transaction, "ALTER TABLE prieres ADD COLUMN refus INTEGER NOT NULL DEFAULT 0;");
        }
        // Version 32 : garder le feu, feux de camp, réserves d'eau et de nourriture.
        if (actuelle < 32) MigrerVersion32(connexion, transaction);
        if (actuelle < 33) MigrerVersion33(connexion, transaction);
        // Version 34 : visites, rassemblements, savoirs venus d'ailleurs, ouvrages demandés et spécialistes.
        if (actuelle < 34) MigrerVersion34(connexion, transaction);
        // Version 35 : journal et pensées enregistrés comme récits (clé et paramètres), écrits dans la langue de l'observateur.
        if (actuelle < 35) MigrerVersion35(connexion, transaction);
        // Version 36 : jardin secret enregistré comme récits.
        if (actuelle < 36) MigrerVersion36(connexion, transaction);
        // Version 37 : génération de la carte (0 pour les mondes d'avant la 0.31.3, dont la carte ne change pas) et mer
        // entre les continents.
        // Version 38 : journal ancien rangé par paquets compressés.
        if (actuelle < 38) MigrerVersion38(connexion, transaction);
        if (actuelle < 37 && !Colonnes(connexion, transaction, "etat").Contains("generation"))
        {
            Executer(connexion, transaction, """
                ALTER TABLE etat ADD COLUMN generation INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE etat ADD COLUMN ecart INTEGER NOT NULL DEFAULT 0;
                """);
        }
        Executer(connexion, transaction, $"PRAGMA user_version = {VersionSchema};");
        transaction.Commit();
    }

    /// <summary>Renvoie l'état enregistré, ou null si le monde n'a jamais été sauvegardé.</summary>
    public EtatMonde? Charger()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT graine, secondes, pas, vitesse, alea0, alea1, alea2, alea3, couples, largeur, hauteur, continents, placement, generation, ecart FROM etat WHERE id = 1;";
        EtatMonde etat;
        using (var lecteur = commande.ExecuteReader())
        {
            if (!lecteur.Read()) return null;
            etat = new EtatMonde(
                unchecked((ulong)lecteur.GetInt64(0)),
                lecteur.GetInt64(1),
                lecteur.GetInt64(2),
                lecteur.GetDouble(3))
            {
                Alea = lecteur.IsDBNull(4) ? null : [.. Enumerable.Range(4, 4).Select(i => unchecked((ulong)lecteur.GetInt64(i)))],
            };
            if (!lecteur.IsDBNull(8))
            {
                var configuration = new ConfigurationMonde(etat.Graine, lecteur.GetInt32(8), lecteur.GetInt32(9), lecteur.GetInt32(10), lecteur.GetInt32(11),
                    lecteur.GetInt32(12), lecteur.GetInt32(13), (EcartContinents)lecteur.GetInt32(14));
                if (configuration.Erreur() is { } erreur) throw new InvalidDataException($"Réglages du monde invalides dans {CheminFichier} : {erreur}");
                etat = etat with { Configuration = configuration };
            }
        }

        commande.CommandText = "SELECT gibier, plantes, degats FROM ressources WHERE id = 1;";
        using (var lecteur = commande.ExecuteReader())
        {
            if (lecteur.Read())
            {
                etat = etat with
                {
                    Gibier = VersUShort((byte[])lecteur[0]), Plantes = VersUShort((byte[])lecteur[1]),
                    DegatsNature = lecteur.IsDBNull(2) ? null : VersUShort((byte[])lecteur[2]),
                };
            }
        }
        commande.CommandText = "SELECT pluies FROM climat WHERE id = 1;";
        if (commande.ExecuteScalar() is byte[] pluies) etat = etat with { Pluies = VersDouble(pluies) };

        commande.CommandText = """
            SELECT id, nom, sexe, partenaire, emplacement, naissance,
                   curiosite, prudence, sociabilite, endurance, adresse,
                   x, y, faim, soif, fatigue, froid, sante, activite,
                   cible_x, cible_y, detour, malade_jusqua, mort_le, cause_mort,
                   mere, pere, desir, enceinte_depuis, pere_enfant, dernier_accouchement,
                   attachement, puberte, fin_fertilite, fertilite, terme_grossesse,
                   agreabilite, nevrosisme, transcendance, qi, qi_verbal, qi_spatial, qi_manuel, qi_memoire, qi_art,
                   humeur, chagrin, deuil_de, jours_sombres, deprime_depuis, communaute, inconfort, compagnie,
                   reserves, metabolisme,
                   faim_soif_jour, froid_jour, jours_seul, colere, deuil_evenement, depression_evenement, mort_evenement, meurtrier,
                   marche, progression, bebe_porte_le,
                   symetrie, harmonie, taille, teint, allure, vers_hommes, vers_femmes, lucidite, maladie,
                   honnetete, empathie_emo, empathie_cog, compassion, psychopathie, anxiete_aff, evitement_aff, soin, equite, loyaute, autorite, purete, captif_de, captif_le,
                   karma, croyance, croyance_signe, croyance_notee,
                   soignes, gueris, perdus, accouchements_aides, guerisseur_depuis, chaman_depuis, transe_le, charlatan_depuis, demasque_le, soigneur, soigneur_le, plaie_le, plaie_soin, maudit_le, maudit_par, maudit_force, maudit_evenement, ancienne_communaute, religion, ferveur, religion_depuis, role, source_prophete, role_depuis, miracles, prie_le, communaute_naissance,
                   amour, crainte, revolte, regard_signe, fecondite_entite, fecondite_jusqua,
                   feux_portes, porteur_du_feu_depuis, feu_demande_le,
                   visite_chez, visite_depuis, visite_arrivee, visite_jusqua, visite_evenement, visite_le, rassemblement, rentrer, venu_de, demande_a, demande_ouvrage, demande_le, servis_taille, servis_assemblage, servis_peau, specialite, specialite_depuis
            FROM humains ORDER BY id;
            """;
        var humains = new List<Humain>();
        using (var lecteur = commande.ExecuteReader())
        {
            while (lecteur.Read())
            {
                var sexe = (Sexe)lecteur.GetByte(2);
                var id = lecteur.GetInt32(0);
                var marche = lecteur.IsDBNull(63) ? Biologie.MarcheDe(id) : lecteur.GetDouble(63);
                var biologie = (lecteur.IsDBNull(32)
                    ? Biologie.ParDefaut(sexe)
                    : new Biologie(lecteur.GetDouble(32), lecteur.GetDouble(33), lecteur.GetDouble(34),
                        lecteur.IsDBNull(54) ? 1 : lecteur.GetDouble(54))) with { Marche = marche };
                humains.Add(new Humain
                {
                    Id = id,
                    Nom = lecteur.GetString(1),
                    Sexe = sexe,
                    Biologie = biologie,
                    PartenaireId = lecteur.GetInt32(3),
                    Emplacement = lecteur.GetInt32(4),
                    Naissance = lecteur.GetInt64(5),
                    Personnalite = LirePersonnalite(lecteur, sexe, etat.Graine, id),
                    X = lecteur.GetInt32(11),
                    Y = lecteur.GetInt32(12),
                    Faim = lecteur.GetDouble(13),
                    Soif = lecteur.GetDouble(14),
                    Fatigue = lecteur.GetDouble(15),
                    Froid = lecteur.GetDouble(16),
                    Sante = lecteur.GetDouble(17),
                    Activite = (Activite)lecteur.GetByte(18),
                    CibleX = lecteur.GetInt32(19),
                    CibleY = lecteur.GetInt32(20),
                    Detour = lecteur.GetBoolean(21),
                    MaladeJusqua = lecteur.GetInt64(22),
                    MortLe = lecteur.IsDBNull(23) ? null : lecteur.GetInt64(23),
                    CauseDeMort = (CauseDeMort)lecteur.GetByte(24),
                    MereId = lecteur.GetInt32(25),
                    PereId = lecteur.GetInt32(26),
                    Desir = lecteur.GetDouble(27),
                    EnceinteDepuis = lecteur.IsDBNull(28) ? null : lecteur.GetInt64(28),
                    PereEnfantId = lecteur.GetInt32(29),
                    DernierAccouchement = lecteur.IsDBNull(30) ? null : lecteur.GetInt64(30),
                    TermeGrossesse = lecteur.IsDBNull(35) ? null : lecteur.GetInt64(35),
                    Humeur = lecteur.GetDouble(45),
                    Chagrin = lecteur.GetDouble(46),
                    DeuilDeId = lecteur.GetInt32(47),
                    JoursSombres = lecteur.GetInt32(48),
                    DeprimeDepuis = lecteur.IsDBNull(49) ? null : lecteur.GetInt64(49),
                    CommunauteId = lecteur.GetInt32(50),
                    InconfortDuJour = lecteur.GetDouble(51),
                    CompagnieDuJour = lecteur.GetInt32(52),
                    Reserves = lecteur.GetDouble(53),
                    FaimSoifDuJour = lecteur.GetDouble(55),
                    FroidDuJour = lecteur.GetDouble(56),
                    JoursSeul = lecteur.GetInt32(57),
                    Colere = lecteur.GetDouble(58),
                    DeuilEvenementId = lecteur.GetInt64(59),
                    DepressionEvenementId = lecteur.GetInt64(60),
                    MortEvenementId = lecteur.GetInt64(61),
                    MeurtrierId = lecteur.GetInt32(62),
                    Progression = lecteur.GetDouble(64),
                    BebePorteLe = lecteur.GetInt64(65),
                    Maladie = (Maladie)lecteur.GetByte(74),
                    CaptifDe = lecteur.GetInt32(lecteur.GetOrdinal("captif_de")),
                    CaptifLe = lecteur.GetInt64(lecteur.GetOrdinal("captif_le")),
                    Karma = lecteur.GetDouble(lecteur.GetOrdinal("karma")),
                    CroyanceSigneLe = lecteur.GetInt64(lecteur.GetOrdinal("croyance_signe")),
                    Soignes = lecteur.GetInt32(lecteur.GetOrdinal("soignes")),
                    Gueris = lecteur.GetInt32(lecteur.GetOrdinal("gueris")),
                    Perdus = lecteur.GetInt32(lecteur.GetOrdinal("perdus")),
                    AccouchementsAides = lecteur.GetInt32(lecteur.GetOrdinal("accouchements_aides")),
                    GuerisseurDepuis = lecteur.GetInt64(lecteur.GetOrdinal("guerisseur_depuis")),
                    ChamanDepuis = lecteur.GetInt64(lecteur.GetOrdinal("chaman_depuis")),
                    TranseLe = lecteur.GetInt64(lecteur.GetOrdinal("transe_le")),
                    CharlatanDepuis = lecteur.GetInt64(lecteur.GetOrdinal("charlatan_depuis")),
                    DemasqueLe = lecteur.GetInt64(lecteur.GetOrdinal("demasque_le")),
                    SoigneurId = lecteur.GetInt32(lecteur.GetOrdinal("soigneur")),
                    SoigneurLe = lecteur.GetInt64(lecteur.GetOrdinal("soigneur_le")),
                    PlaieLe = lecteur.GetInt64(lecteur.GetOrdinal("plaie_le")),
                    PlaieSoin = lecteur.GetDouble(lecteur.GetOrdinal("plaie_soin")),
                    MauditLe = lecteur.GetInt64(lecteur.GetOrdinal("maudit_le")),
                    MauditParId = lecteur.GetInt32(lecteur.GetOrdinal("maudit_par")),
                    MauditForce = lecteur.GetDouble(lecteur.GetOrdinal("maudit_force")),
                    MauditEvenementId = lecteur.GetInt64(lecteur.GetOrdinal("maudit_evenement")),
                    AncienneCommunauteId = lecteur.GetInt32(lecteur.GetOrdinal("ancienne_communaute")),
                    ReligionId = lecteur.GetInt32(lecteur.GetOrdinal("religion")),
                    Ferveur = lecteur.GetDouble(lecteur.GetOrdinal("ferveur")),
                    ReligionDepuis = lecteur.GetInt64(lecteur.GetOrdinal("religion_depuis")),
                    Role = (RoleReligieux)lecteur.GetInt32(lecteur.GetOrdinal("role")),
                    SourceProphete = (SourceProphete)lecteur.GetInt32(lecteur.GetOrdinal("source_prophete")),
                    RoleDepuis = lecteur.GetInt64(lecteur.GetOrdinal("role_depuis")),
                    Miracles = lecteur.GetInt32(lecteur.GetOrdinal("miracles")),
                    PrieLe = lecteur.GetInt64(lecteur.GetOrdinal("prie_le")),
                    CommunauteNaissance = lecteur.GetInt32(lecteur.GetOrdinal("communaute_naissance")),
                    Amour = lecteur.GetDouble(lecteur.GetOrdinal("amour")),
                    Crainte = lecteur.GetDouble(lecteur.GetOrdinal("crainte")),
                    Revolte = lecteur.GetDouble(lecteur.GetOrdinal("revolte")),
                    RegardSigneLe = lecteur.GetInt64(lecteur.GetOrdinal("regard_signe")),
                    FeconditeEntite = lecteur.GetDouble(lecteur.GetOrdinal("fecondite_entite")),
                    FeuxPortes = lecteur.GetInt32(lecteur.GetOrdinal("feux_portes")),
                    PorteurDuFeuDepuis = lecteur.GetInt64(lecteur.GetOrdinal("porteur_du_feu_depuis")),
                    FeuDemandeLe = lecteur.GetInt64(lecteur.GetOrdinal("feu_demande_le")),
                    VisiteChezId = lecteur.GetInt32(lecteur.GetOrdinal("visite_chez")),
                    VisiteDepuis = lecteur.GetInt64(lecteur.GetOrdinal("visite_depuis")),
                    VisiteArriveeLe = lecteur.GetInt64(lecteur.GetOrdinal("visite_arrivee")),
                    VisiteJusquA = lecteur.GetInt64(lecteur.GetOrdinal("visite_jusqua")),
                    VisiteEvenementId = lecteur.GetInt64(lecteur.GetOrdinal("visite_evenement")),
                    VisiteLe = lecteur.GetInt64(lecteur.GetOrdinal("visite_le")),
                    RassemblementId = lecteur.GetInt32(lecteur.GetOrdinal("rassemblement")),
                    Rentrer = lecteur.GetInt32(lecteur.GetOrdinal("rentrer")) != 0,
                    VenuDeId = lecteur.GetInt32(lecteur.GetOrdinal("venu_de")),
                    DemandeAId = lecteur.GetInt32(lecteur.GetOrdinal("demande_a")),
                    DemandeOuvrage = (Ouvrage)lecteur.GetInt32(lecteur.GetOrdinal("demande_ouvrage")),
                    DemandeLe = lecteur.GetInt64(lecteur.GetOrdinal("demande_le")),
                    ServisTaille = lecteur.GetInt32(lecteur.GetOrdinal("servis_taille")),
                    ServisAssemblage = lecteur.GetInt32(lecteur.GetOrdinal("servis_assemblage")),
                    ServisPeau = lecteur.GetInt32(lecteur.GetOrdinal("servis_peau")),
                    Specialite = (Ouvrage)lecteur.GetInt32(lecteur.GetOrdinal("specialite")),
                    SpecialiteDepuis = lecteur.GetInt64(lecteur.GetOrdinal("specialite_depuis")),
                    FeconditeJusqua = lecteur.GetInt64(lecteur.GetOrdinal("fecondite_jusqua")),
                });
                var h = humains[^1];
                var croyance = lecteur.GetOrdinal("croyance");
                h.Croyance = lecteur.IsDBNull(croyance) ? Croyances.Initiale(id, h.Personnalite.Transcendance) : lecteur.GetDouble(croyance);
                var notee = lecteur.GetOrdinal("croyance_notee");
                h.CroyanceNotee = lecteur.IsDBNull(notee) ? h.Croyance : lecteur.GetDouble(notee);
                if (lecteur.IsDBNull(66))
                {
                    // Monde enregistré avant que ces traits existent : ils sont tirés du numéro de l'humain.
                    (h.Apparence, h.Orientation, h.Lucidite) = Attirance.De(id, sexe, h.Personnalite.Ouverture);
                }
                else
                {
                    h.Apparence = new Apparence(lecteur.GetDouble(66), lecteur.GetDouble(67), lecteur.GetDouble(68), lecteur.GetDouble(69),
                        lecteur.GetDouble(70));
                    h.Orientation = new Orientation(lecteur.GetDouble(71), lecteur.GetDouble(72));
                    h.Lucidite = lecteur.GetDouble(73);
                }
            }
        }
        if (humains.Count > 0)
        {
            LireMemoires(connexion, humains);
            LireSanteNature(commande, humains);
            LireSavoirs(commande, humains);
            LireLangage(commande, humains);
        }
        var (foyers, huttes, decouvertes, oeuvres) = LireInstallations(commande);
        var (braises, reserves) = LireFeuxEtReserves(commande);
        if (braises.Count > 0) etat = etat with { Braises = braises };
        if (reserves.Count > 0) etat = etat with { Reserves = reserves };
        var progres = LireProgres(commande);
        if (progres.Count > 0) etat = etat with { Progres = progres };
        var rassemblements = LireRassemblements(commande);
        if (rassemblements.Count > 0) etat = etat with { Rassemblements = rassemblements };
        if (oeuvres.Count > 0) etat = etat with { Oeuvres = oeuvres };
        if (foyers.Count > 0) etat = etat with { Foyers = foyers };
        if (huttes.Count > 0) etat = etat with { Huttes = huttes };
        if (decouvertes.Count > 0) etat = etat with { Decouvertes = decouvertes };
        etat = LireCultes(commande, etat);
        etat = LireGestes(commande, etat);
        var catastrophes = LireCatastrophesEnCours(commande);
        if (catastrophes.Count > 0) etat = etat with { Catastrophes = catastrophes };
        var pathogenes = LirePathogenes(commande);
        if (pathogenes.Count > 0) etat = etat with { Pathogenes = pathogenes };

        commande.CommandText = """
            SELECT id, nom, fondee_le, dispersee_le, figure, centre_x, centre_y, rayon, abondance, disette, destination_x, destination_y,
                   tolerance, usage, derniere_affaire, disette_evenement, nom_propre, sens, sens_cle, eponyme, lexique, origine,
                   fusion_vers, fusion_decidee_le, fusion_evenement, fusionnee_dans, accueil_le, migrations, disettes_surmontees,
                   installation_evenement, territoire, bellicisme, raid, dernier_raid_le, soumise_a, soumise_le,
                   soumission_evenement, paix_evenement, heritiere, projet,
                   ideal_taille, ideal_teint, ideal_corpulence, ideal_note, tolerance_moeurs, usage_moeurs, affaire_moeurs,
                   incendies_souvenus, idee_reserve, idee_reserve_le, idee_reserve_evenement, partages_reserve, vols_reserve, vol_reserve_evenement
            FROM communautes ORDER BY id;
            """;
        var communautes = new List<Communaute>();
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                communautes.Add(new Communaute
                {
                    Id = l.GetInt32(0), Nom = l.GetString(1), FondeeLe = l.GetInt64(2),
                    DisperseeLe = l.IsDBNull(3) ? null : l.GetInt64(3), FigureId = l.GetInt32(4),
                    CentreX = l.GetInt32(5), CentreY = l.GetInt32(6), Rayon = l.GetInt32(7), Abondance = l.GetDouble(8),
                    Disette = l.GetBoolean(9), DestinationX = l.GetInt32(10), DestinationY = l.GetInt32(11),
                    Tolerance = l.GetDouble(12), Usage = (UsageFidelite)l.GetInt32(13), DerniereAffaireId = l.GetInt64(14),
                    DisetteEvenementId = l.GetInt64(15),
                    NomPropre = l.IsDBNull(16) ? null : l.GetString(16), Sens = l.IsDBNull(17) ? null : l.GetString(17),
                    SensCle = l.IsDBNull(18) ? null : l.GetString(18), EponymeId = l.GetInt32(19), Lexique = LireLexique(l.GetString(20)),
                    OrigineId = l.GetInt32(21), FusionVersId = l.GetInt32(22), FusionDecideeLe = l.GetInt64(23),
                    FusionEvenementId = l.GetInt64(24), FusionneeDansId = l.GetInt32(25), AccueilLe = l.IsDBNull(26) ? null : l.GetInt64(26),
                    Migrations = l.GetInt32(27), DisettesSurmontees = l.GetInt32(28), InstallationEvenementId = l.GetInt64(29),
                    Territoire = l.IsDBNull(30) ? [] : LireTerritoire((byte[])l[30]),
                    Bellicisme = l.GetDouble(31), Raid = l.IsDBNull(32) ? null : JsonSerializer.Deserialize<Raid>(l.GetString(32)),
                    DernierRaidLe = l.GetInt64(33), SoumiseAId = l.GetInt32(34), SoumiseLe = l.GetInt64(35),
                    SoumissionEvenementId = l.GetInt64(36), PaixEvenementId = l.GetInt64(37), HeritiereId = l.GetInt32(38),
                    Projet = l.IsDBNull(39) ? null : JsonSerializer.Deserialize<ProjetGuerre>(l.GetString(39)),
                    // Enregistrée avant que ces mœurs existent : −1, elles naîtront du tempérament des membres (voir VieSociale.Moeurs).
                    Ideal = l.IsDBNull(40) ? IdealBeaute.ParDefaut : new IdealBeaute(l.GetDouble(40), l.GetDouble(41), l.GetDouble(42)),
                    IdealNote = l.GetInt32(43),
                    ToleranceMoeurs = l.IsDBNull(44) ? -1 : l.GetDouble(44),
                    UsageMoeurs = (UsageMoeurs)l.GetInt32(45),
                    AffaireMoeursId = l.GetInt64(46),
                    IncendiesSouvenus = LireNumeros(l.GetString(47)),
                    IdeeReserve = (IdeeReserve)l.GetInt32(48), IdeeReserveLe = l.GetInt64(49), IdeeReserveEvenementId = l.GetInt64(50),
                    PartagesReserve = l.GetDouble(51), VolsReserve = l.GetDouble(52), VolReserveEvenementId = l.GetInt64(53),
                });
            }
        }
        if (communautes.Count > 0)
        {
            LireNoms(commande, communautes);
            LireLangues(commande, communautes);
            etat = etat with { Communautes = communautes };
        }

        commande.CommandText = "SELECT a, b, etat, evenement, pacte FROM relations_communautes ORDER BY a, b;";
        var relations = new List<RelationCommunautes>();
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                relations.Add(new RelationCommunautes(l.GetInt32(0), l.GetInt32(1), (EtatRelation)l.GetByte(2), l.GetInt64(3), l.GetInt64(4)));
            }
        }
        if (relations.Count > 0) etat = etat with { Relations = relations };

        commande.CommandText = """
            SELECT id, a, b, debut, fin, morts_a, morts_b, raids, issue, dernier_sang, evenement, fin_evenement, vainqueur
            FROM guerres ORDER BY id;
            """;
        var guerres = new List<Guerre>();
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                guerres.Add(new Guerre
                {
                    Id = l.GetInt32(0), A = l.GetInt32(1), B = l.GetInt32(2), Debut = l.GetInt64(3),
                    Fin = l.IsDBNull(4) ? null : l.GetInt64(4), MortsA = l.GetInt32(5), MortsB = l.GetInt32(6), Raids = l.GetInt32(7),
                    Issue = (IssueGuerre)l.GetByte(8), DernierSangLe = l.GetInt64(9), EvenementId = l.GetInt64(10),
                    FinEvenementId = l.GetInt64(11), VainqueurId = l.GetInt32(12),
                });
            }
        }
        if (guerres.Count > 0) etat = etat with { Guerres = guerres };

        commande.CommandText = "SELECT a, b, evenement FROM offenses_communautes ORDER BY a, b;";
        var offenses = new List<OffenseCommunautes>();
        using (var l = commande.ExecuteReader())
        {
            while (l.Read()) offenses.Add(new OffenseCommunautes(l.GetInt32(0), l.GetInt32(1), l.GetInt64(2)));
        }
        if (offenses.Count > 0) etat = etat with { Offenses = offenses };

        commande.CommandText = "SELECT max(coalesce((SELECT max(id) FROM journal), 0), coalesce((SELECT max(dernier) FROM journal_blocs), 0)) + 1;";
        etat = etat with { ProchainIdJournal = Convert.ToInt64(commande.ExecuteScalar()) };
        return humains.Count > 0 ? etat with { Humains = humains } : etat;
    }

    /// <summary>
    /// Caractère d'un humain. Un monde enregistré avant la version 7 n'avait que cinq traits : la curiosité devient
    /// l'ouverture, la sociabilité l'extraversion, la prudence la conscience ; les nouveaux traits sont moyens.
    /// </summary>
    private static Personnalite LirePersonnalite(SqliteDataReader l, Sexe sexe, ulong graine, int id)
    {
        var p = LireCaractere(l);
        if (l.IsDBNull(75))
        {
            // Monde enregistré avant la version 18 : les nouveaux traits sont tirés d'après ceux qu'il a déjà,
            // d'un flux propre à chaque humain (le reste du monde n'en dépend pas, et une relecture donne les mêmes).
            return Psyche.Completer(p, sexe, new Alea(graine ^ (0x4D4F52414CUL + (ulong)id * 0x9E3779B97F4A7C15UL)));
        }
        double V(int i) => l.GetDouble(i);
        return p with
        {
            Moral = new TraitsMoraux(V(75), V(76), V(77), V(78), V(79), V(80), V(81), new FondementsMoraux(V(82), V(83), V(84), V(85), V(86))),
        };
    }

    private static Personnalite LireCaractere(SqliteDataReader l)
    {
        var attachement = l.IsDBNull(31) ? 0.8 : l.GetDouble(31);
        if (l.IsDBNull(36))
        {
            return new Personnalite(l.GetDouble(6), l.GetDouble(7), l.GetDouble(8), 0.5, 0.5, 0.5, attachement, l.GetDouble(9),
                Intelligence.Moyenne with { Manuelle = 55 + 90 * l.GetDouble(10) });
        }
        return new Personnalite(l.GetDouble(6), l.GetDouble(7), l.GetDouble(8), l.GetDouble(36), l.GetDouble(37), l.GetDouble(38),
            attachement, l.GetDouble(9),
            new Intelligence(l.GetDouble(39), l.GetDouble(40), l.GetDouble(41), l.GetDouble(42), l.GetDouble(43), l.GetDouble(44)));
    }

    private static void LireMemoires(SqliteConnection connexion, List<Humain> humains)
    {
        Humain? Trouver(long id) => id >= 1 && id <= humains.Count ? humains[(int)id - 1] : null;
        using var commande = connexion.CreateCommand();
        // Les rangs gardent l'ordre des listes, donc la simulation reprend à l'identique.
        commande.CommandText = "SELECT humain, type, x, y, vu_le, rappels, decu_le FROM memoire_lieux ORDER BY humain, rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                Trouver(l.GetInt64(0))?.Memoire.Lieux.Add(new LieuConnu((TypeLieu)l.GetByte(1), l.GetInt32(2), l.GetInt32(3), l.GetInt64(4),
                    l.GetInt32(5), l.IsDBNull(6) ? null : l.GetInt64(6)));
            }
        }
        commande.CommandText = "SELECT humain, autre, familiarite, affection, derniere, amis, rivaux, bienfait FROM liens ORDER BY humain, rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                Trouver(l.GetInt64(0))?.Liens.Add(new Lien(l.GetInt32(1), l.GetDouble(2), l.GetDouble(3), l.GetInt64(4),
                    l.GetBoolean(5), l.GetBoolean(6), l.GetInt64(7)));
            }
        }
        var formesPensees = FormesRecits.Lire(connexion, null);
        commande.CommandText = "SELECT humain, secondes, texte, forme, valeurs FROM pensees ORDER BY humain, rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read()) Trouver(l.GetInt64(0))?.Memoire.Pensees.Add(formesPensees.Lire(l, 3, 4) is { } r ? new Pensee(l.GetInt64(1), null, r) : new Pensee(l.GetInt64(1), l.GetString(2)));
        }
        commande.CommandText = "SELECT humain, secondes, type, sujet, texte, intensite, cause, cle, forme, valeurs FROM jardin ORDER BY humain, rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                var recit = formesPensees.Lire(l, 8, 9);
                Trouver(l.GetInt64(0))?.Memoire.Jardin.Add(new Intime(l.GetInt64(1), (TypeIntime)l.GetByte(2), l.GetInt32(3), recit is null ? l.GetString(4) : null,
                    l.GetDouble(5), l.GetInt64(6), l.GetString(7), recit));
            }
        }
        commande.CommandText = "SELECT humain, type, de, le, jusqua, evenement, raison, levee FROM exclusions ORDER BY humain, rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                Trouver(l.GetInt64(0))?.Exclusions.Add(new Exclusion((TypeExclusion)l.GetByte(1), l.GetInt32(2), l.GetInt64(3), l.GetInt64(4),
                    l.GetInt64(5), l.GetString(6), l.GetBoolean(7)));
            }
        }
        commande.CommandText = "SELECT humain, contre, motif, intensite, initiale, evenement, victime FROM griefs ORDER BY humain, rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read())
            {
                Trouver(l.GetInt64(0))?.Griefs.Add(new Grief(l.GetInt32(1), (MotifGrief)l.GetByte(2), l.GetDouble(3), l.GetDouble(4),
                    l.GetInt64(5), l.GetInt32(6)));
            }
        }
        commande.CommandText = "SELECT humain, type, autre, poids FROM raisons ORDER BY humain, rang;";
        using (var l = commande.ExecuteReader())
        {
            while (l.Read()) Trouver(l.GetInt64(0))?.Raisons.Add(new Raison((TypeRaison)l.GetByte(1), l.GetInt32(2), l.GetDouble(3)));
        }
    }

    /// <summary>
    /// Enregistre l'état. Avec <paramref name="remplacer"/>, l'ancien monde est d'abord entièrement effacé
    /// (dans la même transaction : en cas d'échec, l'ancien monde reste intact).
    /// </summary>
    public void Enregistrer(EtatMonde etat, bool remplacer = false)
    {
        using var connexion = Ouvrir();
        using var transaction = connexion.BeginTransaction();
        var formes = FormesRecits.Lire(connexion, transaction);
        if (remplacer)
        {
            Executer(connexion, transaction,
                "DELETE FROM humains; DELETE FROM ressources; DELETE FROM etat; DELETE FROM journal; DELETE FROM journal_blocs; DELETE FROM journal_blocs_cles; DELETE FROM journal_suivis; DELETE FROM journal_purge; " +
                "DELETE FROM memoire_lieux; DELETE FROM liens; DELETE FROM pensees; DELETE FROM communautes; " +
                "DELETE FROM climat; DELETE FROM relations_communautes; DELETE FROM griefs; DELETE FROM raisons; " +
                "DELETE FROM offenses_communautes; DELETE FROM noms_communautes; DELETE FROM archives_communautes; " +
                "DELETE FROM catastrophes; DELETE FROM pathogenes; DELETE FROM guerres; DELETE FROM jardin; DELETE FROM exclusions; " + EffacerSavoirs + EffacerLangage + EffacerCultes + EffacerGestes);
        }

        using (var commande = connexion.CreateCommand())
        {
            commande.Transaction = transaction;
            commande.CommandText = """
                INSERT INTO etat (id, graine, secondes, pas, vitesse, modifie_le, alea0, alea1, alea2, alea3,
                                  couples, largeur, hauteur, continents, placement, generation, ecart)
                VALUES (1, $graine, $secondes, $pas, $vitesse, $modifie, $a0, $a1, $a2, $a3,
                        $couples, $largeur, $hauteur, $continents, $placement, $generation, $ecart)
                ON CONFLICT (id) DO UPDATE SET
                    graine = excluded.graine, secondes = excluded.secondes, pas = excluded.pas,
                    vitesse = excluded.vitesse, modifie_le = excluded.modifie_le,
                    alea0 = excluded.alea0, alea1 = excluded.alea1, alea2 = excluded.alea2, alea3 = excluded.alea3,
                    couples = excluded.couples, largeur = excluded.largeur, hauteur = excluded.hauteur,
                    continents = excluded.continents, placement = excluded.placement,
                    generation = excluded.generation, ecart = excluded.ecart;
                """;
            commande.Parameters.AddWithValue("$graine", unchecked((long)etat.Graine));
            commande.Parameters.AddWithValue("$secondes", etat.Secondes);
            commande.Parameters.AddWithValue("$pas", etat.Pas);
            commande.Parameters.AddWithValue("$vitesse", etat.Facteur);
            commande.Parameters.AddWithValue("$modifie", DateTimeOffset.UtcNow.ToString("O"));
            for (var i = 0; i < 4; i++)
            {
                commande.Parameters.AddWithValue($"$a{i}", etat.Alea is { } a ? unchecked((long)a[i]) : DBNull.Value);
            }
            var c = etat.Configuration;
            commande.Parameters.AddWithValue("$couples", c is null ? DBNull.Value : (object)c.Couples);
            commande.Parameters.AddWithValue("$largeur", c is null ? DBNull.Value : (object)c.Largeur);
            commande.Parameters.AddWithValue("$hauteur", c is null ? DBNull.Value : (object)c.Hauteur);
            commande.Parameters.AddWithValue("$continents", c is null ? DBNull.Value : (object)c.Continents);
            commande.Parameters.AddWithValue("$placement", c?.Placement ?? 1);
            commande.Parameters.AddWithValue("$generation", c?.Generation ?? 0);
            commande.Parameters.AddWithValue("$ecart", (int)(c?.Ecart ?? EcartContinents.Proches));
            commande.ExecuteNonQuery();
        }

        if (etat.Gibier is { } gibier && etat.Plantes is { } plantes)
        {
            using var commande = connexion.CreateCommand();
            commande.Transaction = transaction;
            commande.CommandText = """
                INSERT INTO ressources (id, gibier, plantes, degats) VALUES (1, $gibier, $plantes, $degats)
                ON CONFLICT (id) DO UPDATE SET gibier = excluded.gibier, plantes = excluded.plantes, degats = excluded.degats;
                """;
            commande.Parameters.AddWithValue("$gibier", VersOctets(gibier));
            commande.Parameters.AddWithValue("$plantes", VersOctets(plantes));
            commande.Parameters.AddWithValue("$degats", etat.DegatsNature is { } degats ? VersOctets(degats) : DBNull.Value);
            commande.ExecuteNonQuery();
        }

        if (etat.Pluies is { } pluiesAEnregistrer)
        {
            using var commande = connexion.CreateCommand();
            commande.Transaction = transaction;
            commande.CommandText = "INSERT INTO climat (id, pluies) VALUES (1, $p) ON CONFLICT (id) DO UPDATE SET pluies = excluded.pluies;";
            commande.Parameters.AddWithValue("$p", VersOctets(pluiesAEnregistrer));
            commande.ExecuteNonQuery();
        }

        if (etat.Humains is { } humains)
        {
            using var commande = connexion.CreateCommand();
            commande.Transaction = transaction;
            commande.CommandText = """
                INSERT OR REPLACE INTO humains (id, nom, sexe, partenaire, emplacement, naissance,
                    curiosite, prudence, sociabilite, endurance, adresse,
                    x, y, faim, soif, fatigue, froid, sante, activite,
                    cible_x, cible_y, detour, malade_jusqua, mort_le, cause_mort,
                    mere, pere, desir, enceinte_depuis, pere_enfant, dernier_accouchement,
                    attachement, puberte, fin_fertilite, fertilite, terme_grossesse,
                    agreabilite, nevrosisme, transcendance, qi, qi_verbal, qi_spatial, qi_manuel, qi_memoire, qi_art,
                    humeur, chagrin, deuil_de, jours_sombres, deprime_depuis, communaute, inconfort, compagnie,
                    reserves, metabolisme,
                    faim_soif_jour, froid_jour, jours_seul, colere, deuil_evenement, depression_evenement, mort_evenement, meurtrier,
                    marche, progression, bebe_porte_le,
                    symetrie, harmonie, taille, teint, allure, vers_hommes, vers_femmes, lucidite, maladie,
                    honnetete, empathie_emo, empathie_cog, compassion, psychopathie, anxiete_aff, evitement_aff, soin, equite, loyaute, autorite, purete, captif_de, captif_le,
                    karma, croyance, croyance_signe, croyance_notee,
                    soignes, gueris, perdus, accouchements_aides, guerisseur_depuis, chaman_depuis, transe_le, charlatan_depuis, demasque_le, soigneur, soigneur_le, plaie_le, plaie_soin, maudit_le, maudit_par, maudit_force, maudit_evenement, ancienne_communaute, religion, ferveur, religion_depuis, role, source_prophete, role_depuis, miracles, prie_le, communaute_naissance,
                    amour, crainte, revolte, regard_signe, fecondite_entite, fecondite_jusqua,
                    feux_portes, porteur_du_feu_depuis, feu_demande_le,
                    visite_chez, visite_depuis, visite_arrivee, visite_jusqua, visite_evenement, visite_le, rassemblement, rentrer, venu_de, demande_a, demande_ouvrage, demande_le, servis_taille, servis_assemblage, servis_peau, specialite, specialite_depuis)
                VALUES ($id, $nom, $sexe, $partenaire, $emplacement, $naissance,
                    $curiosite, $prudence, $sociabilite, $endurance, $adresse,
                    $x, $y, $faim, $soif, $fatigue, $froid, $sante, $activite,
                    $cible_x, $cible_y, $detour, $malade_jusqua, $mort_le, $cause_mort,
                    $mere, $pere, $desir, $enceinte_depuis, $pere_enfant, $dernier_accouchement,
                    $attachement, $puberte, $fin_fertilite, $fertilite, $terme_grossesse,
                    $agreabilite, $nevrosisme, $transcendance, $qi, $qi_verbal, $qi_spatial, $qi_manuel, $qi_memoire, $qi_art,
                    $humeur, $chagrin, $deuil_de, $jours_sombres, $deprime_depuis, $communaute, $inconfort, $compagnie,
                    $reserves, $metabolisme,
                    $faim_soif_jour, $froid_jour, $jours_seul, $colere, $deuil_evenement, $depression_evenement, $mort_evenement, $meurtrier,
                    $marche, $progression, $bebe_porte_le,
                    $symetrie, $harmonie, $taille, $teint, $allure, $vers_hommes, $vers_femmes, $lucidite, $maladie,
                    $honnetete, $empathie_emo, $empathie_cog, $compassion, $psychopathie, $anxiete_aff, $evitement_aff, $soin, $equite, $loyaute, $autorite, $purete, $captif_de, $captif_le,
                    $karma, $croyance, $croyance_signe, $croyance_notee,
                    $soignes, $gueris, $perdus, $accouchements_aides, $guerisseur_depuis, $chaman_depuis, $transe_le, $charlatan_depuis, $demasque_le, $soigneur, $soigneur_le, $plaie_le, $plaie_soin, $maudit_le, $maudit_par, $maudit_force, $maudit_evenement, $ancienne_communaute, $religion, $ferveur, $religion_depuis, $role, $source_prophete, $role_depuis, $miracles, $prie_le, $communaute_naissance,
                    $amour, $crainte, $revolte, $regard_signe, $fecondite_entite, $fecondite_jusqua,
                    $feux_portes, $porteur_du_feu_depuis, $feu_demande_le,
                    $visite_chez, $visite_depuis, $visite_arrivee, $visite_jusqua, $visite_evenement, $visite_le, $rassemblement, $rentrer, $venu_de, $demande_a, $demande_ouvrage, $demande_le, $servis_taille, $servis_assemblage, $servis_peau, $specialite, $specialite_depuis);
                """;
            string[] noms = ["$id", "$nom", "$sexe", "$partenaire", "$emplacement", "$naissance",
                "$curiosite", "$prudence", "$sociabilite", "$endurance", "$adresse",
                "$x", "$y", "$faim", "$soif", "$fatigue", "$froid", "$sante", "$activite",
                "$cible_x", "$cible_y", "$detour", "$malade_jusqua", "$mort_le", "$cause_mort",
                "$mere", "$pere", "$desir", "$enceinte_depuis", "$pere_enfant", "$dernier_accouchement",
                "$attachement", "$puberte", "$fin_fertilite", "$fertilite", "$terme_grossesse",
                "$agreabilite", "$nevrosisme", "$transcendance", "$qi", "$qi_verbal", "$qi_spatial", "$qi_manuel", "$qi_memoire", "$qi_art",
                "$humeur", "$chagrin", "$deuil_de", "$jours_sombres", "$deprime_depuis", "$communaute", "$inconfort", "$compagnie",
                "$reserves", "$metabolisme",
                "$faim_soif_jour", "$froid_jour", "$jours_seul", "$colere", "$deuil_evenement", "$depression_evenement", "$mort_evenement", "$meurtrier",
                "$marche", "$progression", "$bebe_porte_le",
                "$symetrie", "$harmonie", "$taille", "$teint", "$allure", "$vers_hommes", "$vers_femmes", "$lucidite", "$maladie",
                "$honnetete", "$empathie_emo", "$empathie_cog", "$compassion", "$psychopathie", "$anxiete_aff", "$evitement_aff", "$soin", "$equite", "$loyaute", "$autorite", "$purete", "$captif_de", "$captif_le",
                "$karma", "$croyance", "$croyance_signe", "$croyance_notee",
                "$soignes", "$gueris", "$perdus", "$accouchements_aides", "$guerisseur_depuis", "$chaman_depuis", "$transe_le", "$charlatan_depuis", "$demasque_le", "$soigneur", "$soigneur_le", "$plaie_le", "$plaie_soin", "$maudit_le", "$maudit_par", "$maudit_force", "$maudit_evenement", "$ancienne_communaute", "$religion", "$ferveur", "$religion_depuis", "$role", "$source_prophete", "$role_depuis", "$miracles", "$prie_le", "$communaute_naissance",
                "$amour", "$crainte", "$revolte", "$regard_signe", "$fecondite_entite", "$fecondite_jusqua",
                "$feux_portes", "$porteur_du_feu_depuis", "$feu_demande_le",
                "$visite_chez", "$visite_depuis", "$visite_arrivee", "$visite_jusqua", "$visite_evenement", "$visite_le", "$rassemblement", "$rentrer", "$venu_de", "$demande_a", "$demande_ouvrage", "$demande_le", "$servis_taille", "$servis_assemblage", "$servis_peau", "$specialite", "$specialite_depuis"];
            var parametres = noms.Select(n => commande.Parameters.Add(new SqliteParameter { ParameterName = n })).ToArray();
            commande.Prepare();
            foreach (var h in humains)
            {
                object[] valeurs =
                [
                    h.Id, h.Nom, (int)h.Sexe, h.PartenaireId, h.Emplacement, h.Naissance,
                    // Colonnes d'origine : curiosite = ouverture, prudence = conscience, sociabilite = extraversion.
                    h.Personnalite.Ouverture, h.Personnalite.Conscience, h.Personnalite.Extraversion,
                    h.Personnalite.Endurance, h.Personnalite.Adresse,
                    h.X, h.Y, h.Faim, h.Soif, h.Fatigue, h.Froid, h.Sante, (int)h.Activite,
                    h.CibleX, h.CibleY, h.Detour, h.MaladeJusqua, h.MortLe is { } m ? m : DBNull.Value, (int)h.CauseDeMort,
                    h.MereId, h.PereId, h.Desir, h.EnceinteDepuis is { } e ? e : DBNull.Value, h.PereEnfantId,
                    h.DernierAccouchement is { } d ? d : DBNull.Value,
                    h.Personnalite.Attachement, h.Biologie.AgePuberte, h.Biologie.AgeFinFertilite, h.Biologie.Fertilite,
                    h.TermeGrossesse is { } t ? t : DBNull.Value,
                    h.Personnalite.Agreabilite, h.Personnalite.Nevrosisme, h.Personnalite.Transcendance,
                    h.Personnalite.Intelligence.Generale, h.Personnalite.Intelligence.Verbale, h.Personnalite.Intelligence.Spatiale,
                    h.Personnalite.Intelligence.Manuelle, h.Personnalite.Intelligence.Memoire, h.Personnalite.Intelligence.Artistique,
                    h.Humeur, h.Chagrin, h.DeuilDeId, h.JoursSombres, h.DeprimeDepuis is { } dd ? dd : DBNull.Value,
                    h.CommunauteId, h.InconfortDuJour, h.CompagnieDuJour,
                    h.Reserves, h.Biologie.Metabolisme,
                    h.FaimSoifDuJour, h.FroidDuJour, h.JoursSeul, h.Colere, h.DeuilEvenementId, h.DepressionEvenementId,
                    h.MortEvenementId, h.MeurtrierId,
                    h.Biologie.Marche, h.Progression, h.BebePorteLe,
                    h.Apparence.Symetrie, h.Apparence.Harmonie, h.Apparence.Taille, h.Apparence.Teint, h.Apparence.Allure,
                    h.Orientation.VersHommes, h.Orientation.VersFemmes, h.Lucidite, (int)h.Maladie,
                    h.Personnalite.Moral.Honnetete, h.Personnalite.Moral.EmpathieEmotionnelle, h.Personnalite.Moral.EmpathieCognitive,
                    h.Personnalite.Moral.Compassion, h.Personnalite.Moral.Psychopathie, h.Personnalite.Moral.AnxieteAffective,
                    h.Personnalite.Moral.EvitementAffectif, h.Personnalite.Moral.Fondements.Soin, h.Personnalite.Moral.Fondements.Equite,
                    h.Personnalite.Moral.Fondements.Loyaute, h.Personnalite.Moral.Fondements.Autorite, h.Personnalite.Moral.Fondements.Purete,
                    h.CaptifDe, h.CaptifLe,
                    h.Karma, h.Croyance, h.CroyanceSigneLe, double.IsNaN(h.CroyanceNotee) ? h.Croyance : h.CroyanceNotee,
                    h.Soignes, h.Gueris, h.Perdus, h.AccouchementsAides, h.GuerisseurDepuis, h.ChamanDepuis, h.TranseLe, h.CharlatanDepuis, h.DemasqueLe, h.SoigneurId, h.SoigneurLe, h.PlaieLe, h.PlaieSoin, h.MauditLe, h.MauditParId, h.MauditForce, h.MauditEvenementId, h.AncienneCommunauteId,
                    h.ReligionId, h.Ferveur, h.ReligionDepuis, (int)h.Role, (int)h.SourceProphete, h.RoleDepuis, h.Miracles, h.PrieLe, h.CommunauteNaissance,
                    h.Amour, h.Crainte, h.Revolte, h.RegardSigneLe, h.FeconditeEntite, h.FeconditeJusqua,
                    h.FeuxPortes, h.PorteurDuFeuDepuis, h.FeuDemandeLe,
                    h.VisiteChezId, h.VisiteDepuis, h.VisiteArriveeLe, h.VisiteJusquA, h.VisiteEvenementId, h.VisiteLe, h.RassemblementId, h.Rentrer ? 1 : 0, h.VenuDeId, h.DemandeAId, (int)h.DemandeOuvrage, h.DemandeLe, h.ServisTaille, h.ServisAssemblage, h.ServisPeau, (int)h.Specialite, h.SpecialiteDepuis,
                ];
                for (var i = 0; i < valeurs.Length; i++) parametres[i].Value = valeurs[i];
                commande.ExecuteNonQuery();
            }
        }

        if (etat.Humains is { } tous)
        {
            EnregistrerMemoires(connexion, transaction, tous, formes);
            EnregistrerSanteNature(connexion, transaction, tous);
            EnregistrerSavoirs(connexion, transaction, tous);
            EnregistrerLangage(connexion, transaction, tous);
        }
        if (etat.Foyers is not null || etat.Huttes is not null || etat.Decouvertes is not null || etat.Oeuvres is not null) EnregistrerInstallations(connexion, transaction, etat);
        if (etat.Braises is not null || etat.Reserves is not null) EnregistrerFeuxEtReserves(connexion, transaction, etat);
        if (etat.Progres is not null) EnregistrerProgres(connexion, transaction, etat.Progres);
        if (etat.Rassemblements is not null) EnregistrerRassemblements(connexion, transaction, etat.Rassemblements);
        if (etat.Religions is not null || etat.Prieres is not null || etat.LieuxSacres is not null || etat.Propheties is not null)
            EnregistrerCultes(connexion, transaction, etat);
        if (etat.EffetsEntite is not null || etat.SouvenirsEntite is not null) EnregistrerGestes(connexion, transaction, etat);
        if (etat.Catastrophes is { Count: > 0 } catastrophes) EnregistrerCatastrophes(connexion, transaction, catastrophes);
        if (etat.Pathogenes is { Count: > 0 } pathogenes) EnregistrerPathogenes(connexion, transaction, pathogenes);
        if (etat.Communautes is { } communautes)
        {
            EnregistrerCommunautes(connexion, transaction, communautes);
            EnregistrerLangues(connexion, transaction, communautes);
        }
        if (etat.Relations is { } relations) EnregistrerRelations(connexion, transaction, relations);
        if (etat.Guerres is { Count: > 0 } guerres) EnregistrerGuerres(connexion, transaction, guerres);
        if (etat.Offenses is { } offensesAEnregistrer) EnregistrerOffenses(connexion, transaction, offensesAEnregistrer);
        if (etat.JournalNonEnregistre is { Count: > 0 } journal) EnregistrerJournal(connexion, transaction, journal, formes);
        if (etat.ArchivesNonEnregistrees is { Count: > 0 } archives) EnregistrerArchives(connexion, transaction, archives);
        formes.Enregistrer(connexion, transaction);

        transaction.Commit();
    }

    private static void EnregistrerMemoires(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Humain> humains,
        FormesRecits formes)
    {
        Executer(connexion, transaction,
            "DELETE FROM memoire_lieux; DELETE FROM liens; DELETE FROM pensees; DELETE FROM griefs; DELETE FROM raisons; DELETE FROM jardin; DELETE FROM exclusions;");
        using var exclusions = Preparer(connexion, transaction,
            "INSERT INTO exclusions (humain, rang, type, de, le, jusqua, evenement, raison, levee) VALUES ($h, $r, $t, $d, $l, $j, $e, $x, $v);",
            "$h", "$r", "$t", "$d", "$l", "$j", "$e", "$x", "$v");
        using var jardin = Preparer(connexion, transaction,
            "INSERT INTO jardin (humain, rang, secondes, type, sujet, texte, intensite, cause, cle, forme, valeurs) VALUES ($h, $r, $s, $t, $su, $x, $i, $c, $k, $f, $v);",
            "$h", "$r", "$s", "$t", "$su", "$x", "$i", "$c", "$k", "$f", "$v");
        using var griefs = Preparer(connexion, transaction,
            "INSERT INTO griefs (humain, rang, contre, motif, intensite, initiale, evenement, victime) VALUES ($h, $r, $c, $m, $i, $n, $e, $v);",
            "$h", "$r", "$c", "$m", "$i", "$n", "$e", "$v");
        using var raisons = Preparer(connexion, transaction,
            "INSERT INTO raisons (humain, rang, type, autre, poids) VALUES ($h, $r, $t, $a, $p);", "$h", "$r", "$t", "$a", "$p");
        using var lieux = Preparer(connexion, transaction,
            "INSERT INTO memoire_lieux (humain, rang, type, x, y, vu_le, rappels, decu_le) VALUES ($h, $r, $t, $x, $y, $v, $n, $d);",
            "$h", "$r", "$t", "$x", "$y", "$v", "$n", "$d");
        using var liens = Preparer(connexion, transaction,
            "INSERT INTO liens (humain, rang, autre, familiarite, affection, derniere, amis, rivaux, bienfait) VALUES ($h, $r, $a, $f, $af, $d, $am, $ri, $bf);",
            "$h", "$r", "$a", "$f", "$af", "$d", "$am", "$ri", "$bf");
        using var pensees = Preparer(connexion, transaction,
            "INSERT INTO pensees (humain, rang, secondes, texte, forme, valeurs) VALUES ($h, $r, $s, $t, $c, $p);", "$h", "$r", "$s", "$t", "$c", "$p");
        foreach (var h in humains)
        {
            var m = h.Memoire;
            for (var r = 0; r < m.Lieux.Count; r++)
            {
                var l = m.Lieux[r];
                Lancer(lieux, h.Id, r, (int)l.Type, l.X, l.Y, l.VuLe, l.Rappels, l.DecuLe is { } d ? d : DBNull.Value);
            }
            for (var r = 0; r < h.Liens.Count; r++)
            {
                var l = h.Liens[r];
                Lancer(liens, h.Id, r, l.Id, l.Familiarite, l.Affection, l.Derniere, l.Amis, l.Rivaux, l.Bienfait);
            }
            for (var r = 0; r < m.Pensees.Count; r++)
            {
                var (texte, forme, valeurs) = formes.Colonnes(m.Pensees[r].Recit, m.Pensees[r].Brut);
                Lancer(pensees, h.Id, r, m.Pensees[r].Secondes, texte, forme, valeurs);
            }
            for (var r = 0; r < m.Jardin.Count; r++)
            {
                var x = m.Jardin[r];
                var (texte, forme, valeurs) = formes.Colonnes(x.Recit, x.Brut);
                Lancer(jardin, h.Id, r, x.Secondes, (int)x.Type, x.SujetId, texte, x.Intensite, x.CauseId, x.Cle, forme, valeurs);
            }
            for (var r = 0; r < h.Griefs.Count; r++)
            {
                var g = h.Griefs[r];
                Lancer(griefs, h.Id, r, g.ContreId, (int)g.Motif, g.Intensite, g.Initiale, g.EvenementId, g.VictimeId);
            }
            for (var r = 0; r < h.Raisons.Count; r++)
            {
                var x = h.Raisons[r];
                Lancer(raisons, h.Id, r, (int)x.Type, x.AutreId, x.Poids);
            }
            for (var r = 0; r < h.Exclusions.Count; r++)
            {
                var x = h.Exclusions[r];
                Lancer(exclusions, h.Id, r, (int)x.Type, x.De, x.Le, x.Jusqua, x.EvenementId, x.Raison, x.Levee);
            }
        }
    }

    private static void EnregistrerCommunautes(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Communaute> communautes)
    {
        using var commande = Preparer(connexion, transaction, """
            INSERT OR REPLACE INTO communautes (id, nom, fondee_le, dispersee_le, figure, centre_x, centre_y, rayon, abondance, disette,
                destination_x, destination_y, tolerance, usage, derniere_affaire, disette_evenement, nom_propre, sens, sens_cle, eponyme,
                lexique, origine, fusion_vers, fusion_decidee_le, fusion_evenement, fusionnee_dans, accueil_le, migrations,
                disettes_surmontees, installation_evenement, territoire, bellicisme, raid, dernier_raid_le, soumise_a, soumise_le,
                soumission_evenement, paix_evenement, heritiere, projet,
                ideal_taille, ideal_teint, ideal_corpulence, ideal_note, tolerance_moeurs, usage_moeurs, affaire_moeurs,
                incendies_souvenus, idee_reserve, idee_reserve_le, idee_reserve_evenement, partages_reserve, vols_reserve, vol_reserve_evenement)
            VALUES ($i, $n, $f, $d, $g, $cx, $cy, $r, $a, $di, $dx, $dy, $to, $u, $da, $de, $np, $s, $sc, $ep, $lx, $o, $fv, $fd, $fe,
                $fi, $al, $mi, $ds, $ie, $te, $be, $ra, $dr, $sa, $sl, $se, $pe, $he, $pr, $it, $iq, $ic, $in, $tm, $um, $am,
                $is, $ir, $irl, $ire, $pres, $vres, $vre);
            """, "$i", "$n", "$f", "$d", "$g", "$cx", "$cy", "$r", "$a", "$di", "$dx", "$dy", "$to", "$u", "$da", "$de", "$np", "$s",
            "$sc", "$ep", "$lx", "$o", "$fv", "$fd", "$fe", "$fi", "$al", "$mi", "$ds", "$ie", "$te", "$be", "$ra", "$dr", "$sa", "$sl",
            "$se", "$pe", "$he", "$pr", "$it", "$iq", "$ic", "$in", "$tm", "$um", "$am", "$is", "$ir", "$irl", "$ire", "$pres", "$vres", "$vre");
        foreach (var c in communautes)
        {
            Lancer(commande, c.Id, c.Nom, c.FondeeLe, c.DisperseeLe is { } d ? d : DBNull.Value, c.FigureId,
                c.CentreX, c.CentreY, c.Rayon, c.Abondance, c.Disette, c.DestinationX, c.DestinationY,
                c.Tolerance, (int)c.Usage, c.DerniereAffaireId, c.DisetteEvenementId,
                (object?)c.NomPropre ?? DBNull.Value, (object?)c.Sens ?? DBNull.Value, (object?)c.SensCle ?? DBNull.Value, c.EponymeId,
                EcrireLexique(c.Lexique), c.OrigineId, c.FusionVersId, c.FusionDecideeLe, c.FusionEvenementId, c.FusionneeDansId,
                c.AccueilLe is { } al ? al : DBNull.Value, c.Migrations, c.DisettesSurmontees, c.InstallationEvenementId,
                c.Territoire.Count == 0 ? DBNull.Value : EcrireTerritoire(c.Territoire),
                c.Bellicisme, c.Raid is { } raid ? JsonSerializer.Serialize(raid) : DBNull.Value, c.DernierRaidLe, c.SoumiseAId,
                c.SoumiseLe, c.SoumissionEvenementId, c.PaixEvenementId, c.HeritiereId,
                c.Projet is { } projet ? JsonSerializer.Serialize(projet) : DBNull.Value,
                c.Ideal.Taille, c.Ideal.Teint, c.Ideal.Corpulence, c.IdealNote,
                c.ToleranceMoeurs < 0 ? DBNull.Value : c.ToleranceMoeurs, (int)c.UsageMoeurs, c.AffaireMoeursId,
                string.Join(',', c.IncendiesSouvenus), (int)c.IdeeReserve, c.IdeeReserveLe, c.IdeeReserveEvenementId,
                c.PartagesReserve, c.VolsReserve, c.VolReserveEvenementId);
        }
        EnregistrerNoms(connexion, transaction, communautes);
    }

    private static void EnregistrerRelations(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<RelationCommunautes> relations)
    {
        Executer(connexion, transaction, "DELETE FROM relations_communautes;");
        using var commande = Preparer(connexion, transaction,
            "INSERT INTO relations_communautes (a, b, etat, evenement, pacte) VALUES ($a, $b, $e, $v, $p);", "$a", "$b", "$e", "$v", "$p");
        foreach (var r in relations) Lancer(commande, r.A, r.B, (int)r.Etat, r.EvenementId, r.PacteEvenementId);
    }

    private static void EnregistrerGuerres(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<Guerre> guerres)
    {
        using var commande = Preparer(connexion, transaction, """
            INSERT OR REPLACE INTO guerres (id, a, b, debut, fin, morts_a, morts_b, raids, issue, dernier_sang, evenement, fin_evenement, vainqueur)
            VALUES ($i, $a, $b, $d, $f, $ma, $mb, $r, $is, $ds, $e, $fe, $v);
            """, "$i", "$a", "$b", "$d", "$f", "$ma", "$mb", "$r", "$is", "$ds", "$e", "$fe", "$v");
        foreach (var g in guerres)
        {
            Lancer(commande, g.Id, g.A, g.B, g.Debut, g.Fin is { } f ? f : DBNull.Value, g.MortsA, g.MortsB, g.Raids, (int)g.Issue,
                g.DernierSangLe, g.EvenementId, g.FinEvenementId, g.VainqueurId);
        }
    }

    private static void EnregistrerOffenses(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<OffenseCommunautes> offenses)
    {
        Executer(connexion, transaction, "DELETE FROM offenses_communautes;");
        using var commande = Preparer(connexion, transaction,
            "INSERT INTO offenses_communautes (a, b, evenement) VALUES ($a, $b, $e);", "$a", "$b", "$e");
        foreach (var o in offenses) Lancer(commande, o.A, o.B, o.EvenementId);
    }

    private static void EnregistrerJournal(SqliteConnection connexion, SqliteTransaction transaction, IReadOnlyList<EvenementJournal> journal,
        FormesRecits formes)
    {
        // INSERT OR IGNORE : un événement déjà enregistré (même numéro) n'est pas dupliqué.
        using var commande = Preparer(connexion, transaction,
            """
            INSERT OR IGNORE INTO journal (id, secondes, type, humain, autre, texte, origine, cause, communaute, autre_communaute, significatif, karma, karma_autre, forme, valeurs)
            VALUES ($i, $s, $t, $h, $a, $x, $o, $c, $co, $ac, $si, $k, $ka, $cl, $pa);
            """,
            "$i", "$s", "$t", "$h", "$a", "$x", "$o", "$c", "$co", "$ac", "$si", "$k", "$ka", "$cl", "$pa");
        foreach (var e in journal)
        {
            var (texte, forme, valeurs) = formes.Colonnes(e.Recit, e.Brut);
            Lancer(commande, e.Id, e.Secondes, (int)e.Type, e.HumainId, e.AutreId, texte, (int)e.Origine, e.CauseId,
                e.CommunauteId, e.AutreCommunauteId, e.Significatif, e.Karma, e.KarmaAutre, forme, valeurs);
        }
    }

    /// <summary>Noms des colonnes d'une table (le nom de table vient du code, jamais de l'extérieur).</summary>
    private static HashSet<string> Colonnes(SqliteConnection connexion, SqliteTransaction transaction, string table)
    {
        using var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
        commande.CommandText = "SELECT name FROM pragma_table_info($table);";
        commande.Parameters.AddWithValue("$table", table);
        var noms = new HashSet<string>();
        using var l = commande.ExecuteReader();
        while (l.Read()) noms.Add(l.GetString(0));
        return noms;
    }

    private static SqliteCommand Preparer(SqliteConnection connexion, SqliteTransaction transaction, string sql, params string[] noms)
    {
        var commande = connexion.CreateCommand();
        commande.Transaction = transaction;
        commande.CommandText = sql;
        foreach (var n in noms) commande.Parameters.Add(new SqliteParameter { ParameterName = n });
        commande.Prepare();
        return commande;
    }

    private static void Lancer(SqliteCommand commande, params object[] valeurs)
    {
        for (var i = 0; i < valeurs.Length; i++) commande.Parameters[i].Value = valeurs[i];
        commande.ExecuteNonQuery();
    }

    /// <summary>Plus grand nombre d'événements renvoyés par une lecture du journal.</summary>
    public const int LimiteJournal = 500;

    /// <summary>
    /// Lit le journal enregistré, du plus récent au plus ancien. Filtres facultatifs : types, humain concerné
    /// (acteur ou autre), communauté, événements significatifs seulement, et pagination par numéro (événements de numéro
    /// inférieur à <paramref name="avantId"/>).
    /// </summary>
    public List<EvenementJournal> LireJournal(IReadOnlyCollection<TypeEvenement>? types = null, int? humain = null,
        long? avantId = null, int limite = 100, int? communaute = null, bool significatifs = false)
    {
        limite = Math.Clamp(limite, 1, LimiteJournal);
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        var conditions = new List<string>();
        if (types is { Count: > 0 })
        {
            // Un paramètre par type : jamais de valeur concaténée dans le SQL.
            var noms = types.Distinct().Select((t, i) =>
            {
                commande.Parameters.AddWithValue($"$t{i}", (int)t);
                return $"$t{i}";
            }).ToList();
            conditions.Add($"type IN ({string.Join(", ", noms)})");
        }
        if (humain is { } h)
        {
            conditions.Add("(humain = $h OR autre = $h)");
            commande.Parameters.AddWithValue("$h", h);
        }
        if (communaute is { } c)
        {
            conditions.Add("(communaute = $c OR autre_communaute = $c)");
            commande.Parameters.AddWithValue("$c", c);
        }
        if (significatifs) conditions.Add("significatif = 1");
        if (avantId is { } a)
        {
            conditions.Add("id < $a");
            commande.Parameters.AddWithValue("$a", a);
        }
        commande.Parameters.AddWithValue("$limite", limite);
        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        commande.CommandText = $"SELECT {ColonnesJournal} FROM journal {where} ORDER BY id DESC LIMIT $limite;";
        // Table et paquets lus dans la même transaction : un paquet rangé entre les deux lectures ne compte pas deux fois.
        using var transaction = connexion.BeginTransaction(deferred: true);
        commande.Transaction = transaction;
        var lignes = new List<LigneJournal>();
        using (var l = commande.ExecuteReader())
        {
            while (l.Read()) lignes.Add(LireLigne(l));
        }
        // Les faits marquants ne sont jamais rangés : pour eux, la table suffit. Sinon, les paquets ne sont ouverts que si
        // la table n'a pas déjà de quoi remplir la page avec des lignes plus récentes que tout ce qui est rangé.
        if (!significatifs)
        {
            using var rangees = connexion.CreateCommand();
            rangees.Transaction = transaction;
            rangees.CommandText = "SELECT coalesce(max(dernier), 0) FROM journal_blocs;";
            var dernierRange = Convert.ToInt64(rangees.ExecuteScalar());
            if (dernierRange > 0 && (lignes.Count < limite || lignes[^1].Id < dernierRange))
                lignes.AddRange(LireArchives(connexion, transaction, types, humain, avantId, limite, communaute));
        }
        var formes = FormesRecits.Lire(connexion, transaction);
        return [.. lignes.OrderByDescending(x => x.Id).Take(limite).Select(x => VersEvenement(x, formes))];
    }

    /// <summary>Un événement du journal enregistré, par son numéro (null s'il n'existe pas).</summary>
    public EvenementJournal? LireEvenement(long id)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        using var transaction = connexion.BeginTransaction(deferred: true);
        commande.Transaction = transaction;
        commande.CommandText = $"SELECT {ColonnesJournal} FROM journal WHERE id = $id;";
        commande.Parameters.AddWithValue("$id", id);
        LigneJournal? ligne = null;
        using (var l = commande.ExecuteReader())
        {
            if (l.Read()) ligne = LireLigne(l);
        }
        ligne ??= LireArchive(connexion, transaction, id);
        return ligne is { } x ? VersEvenement(x, FormesRecits.Lire(connexion, transaction)) : null;
    }

    /// <summary>Lit un réglage de l'application (null s'il n'a jamais été enregistré).</summary>
    public string? LireReglage(string cle)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT valeur FROM reglages WHERE cle = $cle;";
        commande.Parameters.AddWithValue("$cle", cle);
        return commande.ExecuteScalar() as string;
    }

    public void EcrireReglage(string cle, string valeur)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "INSERT INTO reglages (cle, valeur) VALUES ($cle, $valeur) ON CONFLICT (cle) DO UPDATE SET valeur = excluded.valeur;";
        commande.Parameters.AddWithValue("$cle", cle);
        commande.Parameters.AddWithValue("$valeur", valeur);
        commande.ExecuteNonQuery();
    }

    /// <summary>
    /// Recommence avec un monde neuf : le fichier actuel devient <paramref name="cheminCopie"/> (un fichier existant
    /// à cet emplacement est remplacé), puis un fichier neuf reçoit <paramref name="etat"/> et garde les réglages.
    /// Renommer le fichier prend un instant quelle que soit la taille du monde, alors qu'une copie (VACUUM INTO)
    /// suivie d'un effacement réécrit tout le fichier. Si le renommage est impossible (fichier ouvert ailleurs,
    /// sous Windows), le monde est copié puis effacé comme avant. L'appelant doit avoir enregistré l'ancien monde.
    /// </summary>
    public void RecommencerApresCopie(string cheminCopie, EtatMonde etat)
    {
        var copie = Path.GetFullPath(cheminCopie);
        if (string.Equals(copie, CheminFichier, StringComparison.Ordinal))
        {
            throw new ArgumentException("La copie doit aller dans un autre fichier.", nameof(cheminCopie));
        }
        if (!Path.GetDirectoryName(copie)!.Equals(Path.GetDirectoryName(CheminFichier), StringComparison.Ordinal))
        {
            throw new ArgumentException("La copie doit rester dans le dossier du monde.", nameof(cheminCopie));
        }

        var reglages = LireReglages();
        if (!Renommer(copie))
        {
            CopierVers(copie);
            Enregistrer(etat, remplacer: true);
            return;
        }
        Initialiser();
        foreach (var (cle, valeur) in reglages) EcrireReglage(cle, valeur);
        Enregistrer(etat);
    }

    /// <summary>
    /// Déplace le fichier du monde vers <paramref name="copie"/>. Le journal d'écriture anticipée (WAL) est d'abord
    /// reversé dans le fichier et vidé : sinon, ce qu'il contient serait perdu pour la copie ou appliqué au fichier neuf.
    /// Rend faux, sans rien changer, si une autre connexion empêche de vider le journal ou de déplacer le fichier.
    /// </summary>
    private bool Renommer(string copie)
    {
        using (var connexion = Ouvrir())
        using (var commande = connexion.CreateCommand())
        {
            commande.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            using var l = commande.ExecuteReader();
            // busy = 0 et journal vide (0 page) : tout est dans le fichier principal.
            if (!l.Read() || l.GetInt32(0) != 0 || l.GetInt32(1) > 0) return false;
        }
        try
        {
            // Un -wal ou -shm laissé par la copie précédente serait relu avec le nouveau fichier copié.
            foreach (var reste in new[] { copie + "-wal", copie + "-shm", copie + "-journal" }) File.Delete(reste);
            File.Move(CheminFichier, copie, overwrite: true);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        // Le journal vidé et l'index partagé de l'ancien fichier ne doivent pas être rattachés au fichier neuf.
        foreach (var reste in new[] { CheminFichier + "-wal", CheminFichier + "-shm" })
        {
            try { File.Delete(reste); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return true;
    }

    /// <summary>
    /// Résumé d'un fichier de monde (version du schéma, graine, date du monde, humains vivants), lu sans le modifier ;
    /// null si ce n'est pas un monde lisible.
    /// </summary>
    public static ResumeMonde? Decrire(string chemin)
    {
        if (!File.Exists(chemin)) return null;
        var texte = new SqliteConnectionStringBuilder { DataSource = chemin, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        try
        {
            using var connexion = new SqliteConnection(texte);
            connexion.Open();
            using var commande = connexion.CreateCommand();
            commande.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(commande.ExecuteScalar());
            commande.CommandText = "SELECT graine, secondes FROM etat WHERE id = 1;";
            using (var l = commande.ExecuteReader())
            {
                if (!l.Read()) return null;
                var (graine, secondes) = (unchecked((ulong)l.GetInt64(0)), l.GetInt64(1));
                l.Close();
                commande.CommandText = "SELECT count(*) FROM humains WHERE mort_le IS NULL;";
                return new ResumeMonde(version, graine, secondes, Convert.ToInt32(commande.ExecuteScalar()));
            }
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    /// <summary>
    /// Prépare la restauration d'une sauvegarde : elle est copiée à côté du monde (la sauvegarde elle-même n'est pas modifiée),
    /// la copie est mise à jour au schéma actuel et lue. Rend le fichier préparé et l'état qu'il contient ;
    /// à passer ensuite à <see cref="RemplacerPar"/>, ou à effacer.
    /// </summary>
    /// <exception cref="InvalidDataException">Le fichier n'est pas un monde lisible.</exception>
    public (string Fichier, EtatMonde Etat) PreparerRestauration(string sauvegarde)
    {
        var prepare = CheminFichier + ".restauration";
        EffacerAvecJournal(prepare);
        File.Copy(sauvegarde, prepare);
        try
        {
            var etat = new DepotMonde(prepare).Charger() ?? throw new InvalidDataException("Cette sauvegarde ne contient pas de monde.");
            return (prepare, etat);
        }
        catch (Exception e) when (e is SqliteException or InvalidDataException or InvalidOperationException)
        {
            EffacerAvecJournal(prepare);
            // Message sans chemin de fichier : il est montré dans le navigateur.
            throw new InvalidDataException(e is InvalidOperationException
                ? "Cette sauvegarde vient d'une version plus récente de l'application."
                : e.Message.StartsWith("Cette sauvegarde", StringComparison.Ordinal) ? e.Message : "Cette sauvegarde est illisible.", e);
        }
    }

    /// <summary>
    /// Remplace le monde par un fichier préparé (<see cref="PreparerRestauration"/>), en gardant les réglages de
    /// l'application. L'appelant doit avoir copié l'ancien monde s'il veut le garder.
    /// </summary>
    public void RemplacerPar(string prepare)
    {
        var reglages = LireReglages();
        using (var connexion = Ouvrir())
        using (var commande = connexion.CreateCommand())
        {
            commande.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            commande.ExecuteNonQuery();
        }
        foreach (var reste in new[] { CheminFichier + "-wal", CheminFichier + "-shm" })
        {
            try { File.Delete(reste); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        File.Move(prepare, CheminFichier, overwrite: true);
        EffacerAvecJournal(prepare);
        Initialiser();
        foreach (var (cle, valeur) in reglages) EcrireReglage(cle, valeur);
    }

    private static void EffacerAvecJournal(string chemin)
    {
        foreach (var f in new[] { chemin, chemin + "-wal", chemin + "-shm", chemin + "-journal" }) File.Delete(f);
    }

    private List<(string Cle, string Valeur)> LireReglages()
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "SELECT cle, valeur FROM reglages;";
        var resultat = new List<(string, string)>();
        using var l = commande.ExecuteReader();
        while (l.Read()) resultat.Add((l.GetString(0), l.GetString(1)));
        return resultat;
    }

    /// <summary>
    /// Copie cohérente du monde dans un autre fichier (VACUUM INTO), même pendant que le serveur écrit.
    /// Un fichier existant à cet emplacement est remplacé.
    /// </summary>
    public void CopierVers(string cheminCopie)
    {
        var complet = Path.GetFullPath(cheminCopie);
        if (string.Equals(complet, Path.GetFullPath(CheminFichier), StringComparison.Ordinal))
        {
            throw new ArgumentException("La copie doit aller dans un autre fichier.", nameof(cheminCopie));
        }
        File.Delete(complet); // VACUUM INTO refuse d'écrire sur un fichier existant
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "VACUUM INTO $chemin;";
        commande.Parameters.AddWithValue("$chemin", complet);
        commande.ExecuteNonQuery();
    }

    // Les ressources sont stockées en petit-boutiste, quel que soit le processeur.
    private static byte[] VersOctets(ushort[] valeurs)
    {
        var octets = new byte[valeurs.Length * 2];
        for (var i = 0; i < valeurs.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(octets.AsSpan(i * 2), valeurs[i]);
        return octets;
    }

    private static byte[] VersOctets(double[] valeurs)
    {
        var octets = new byte[valeurs.Length * 8];
        for (var i = 0; i < valeurs.Length; i++) BinaryPrimitives.WriteDoubleLittleEndian(octets.AsSpan(i * 8), valeurs[i]);
        return octets;
    }

    private static double[] VersDouble(byte[] octets)
    {
        if (octets.Length % 8 != 0) throw new InvalidDataException("Pluies corrompues dans la sauvegarde.");
        var valeurs = new double[octets.Length / 8];
        for (var i = 0; i < valeurs.Length; i++) valeurs[i] = BinaryPrimitives.ReadDoubleLittleEndian(octets.AsSpan(i * 8));
        return valeurs;
    }

    private static ushort[] VersUShort(byte[] octets)
    {
        if (octets.Length % 2 != 0) throw new InvalidDataException("Ressources corrompues dans la sauvegarde.");
        var valeurs = new ushort[octets.Length / 2];
        for (var i = 0; i < valeurs.Length; i++) valeurs[i] = BinaryPrimitives.ReadUInt16LittleEndian(octets.AsSpan(i * 2));
        return valeurs;
    }
}

/// <summary>Ce qu'un fichier de monde contient, en bref : version du schéma, graine, date du monde, humains vivants.</summary>
public sealed record ResumeMonde(int Version, ulong Graine, long Secondes, int Vivants);
