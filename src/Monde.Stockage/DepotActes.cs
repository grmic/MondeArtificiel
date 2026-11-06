using Microsoft.Data.Sqlite;

namespace Monde.Stockage;

/// <summary>
/// Un acte de l'Entité, joué par le joueur ou par le LLM : ce qu'elle a fait, sur qui, et ce que les règles en ont fait.
/// </summary>
/// <param name="Id">Numéro (0 avant l'enregistrement).</param>
/// <param name="Le">Date du monde (secondes).</param>
/// <param name="Auteur">0 le joueur, 1 le LLM.</param>
/// <param name="Sorte">parler, parler_communaute, geste, catastrophe, exaucer, refuser, souffler_geste, souffler_combinaison, penchant,
/// exaucer_toutes, refuser_toutes, reglage.</param>
/// <param name="Genre">Précision : nom du geste, de la parole ou de la catastrophe (vide sinon).</param>
/// <param name="Humain">Humain visé (0 : aucun).</param>
/// <param name="Communaute">Communauté visée (0 : aucune).</param>
/// <param name="Priere">Prière (0 : aucune).</param>
/// <param name="Nombre">Humains (ou prières) touchés.</param>
/// <param name="Acte">L'acte codé (JSON, vide pour un acte repris du journal).</param>
/// <param name="Resultats">Ce que les règles en ont fait (JSON : liste de textes ; vide pour un acte repris : lire le journal).</param>
/// <param name="Issue">1 : il a eu son effet ; 0 : sans effet visible ; −1 : refusé par les règles.</param>
/// <param name="JournalPremier">Premier numéro du journal écrit (0 : aucun).</param>
/// <param name="JournalDernier">Dernier numéro du journal écrit (−1 : aucun).</param>
/// <param name="Echange">Échange du LLM qui l'a décidé (0 : aucun).</param>
/// <param name="Repris">Reconstitué depuis le journal (actes d'avant la 0.35.1).</param>
/// <param name="Defaire">Pour un acte libre : de quoi le défaire (JSON ; vide sinon).</param>
/// <param name="Defait">Acte libre défait par le joueur.</param>
public sealed record ActeEntite(long Id, long Le, int Auteur, string Sorte, string Genre, int Humain, int Communaute, int Priere, int Nombre,
    string Acte, string Resultats, int Issue, long JournalPremier, long JournalDernier, long Echange, bool Repris, string Defaire = "", bool Defait = false);

/// <summary>Filtre des actes de l'Entité (null ou 0 : tous).</summary>
public sealed record FiltreActes(int? Auteur = null, string? Sorte = null, int Humain = 0, int Communaute = 0, long Depuis = 0, long Jusqua = 0,
    long Avant = 0, int Limite = 200);

/// <summary>Comptes des actes de l'Entité, pour l'en-tête de la fenêtre.</summary>
public sealed record ComptesActes(int Total, int ParLlm, int Exaucees, int Refusees, int Morts, int Bienfaits, int Chatiments, int Paroles, int Souffles,
    int SoufflesReussis, int Catastrophes, IReadOnlyList<(int An, int Joueur, int Llm)> ParAn);

/// <summary>Chronologie des actes de l'Entité (table <c>entite_actes</c>).</summary>
public sealed partial class DepotMonde
{
    private const string ColonnesActes =
        "id, le, auteur, sorte, genre, humain, communaute, priere, nombre, acte, resultats, issue, journal_premier, journal_dernier, echange, repris, defaire, defait";

    /// <summary>Gestes de l'Entité qui font du bien (les autres sont des châtiments), par leur nom.</summary>
    public static readonly IReadOnlySet<string> GestesBienfaits = new HashSet<string> { "Guerir", "RendreFecond", "SortirDeFamine", "ArreterEpidemie", "Fecondite", "Benir" };

    /// <summary>
    /// Migration vers la version 51 : chronologie des actes de l'Entité. Les actes d'avant sont reconstitués depuis le journal
    /// (lignes marquées « Entité » encore dans la table ; les paquets rangés ne sont pas ouverts).
    /// </summary>
    private static void MigrerVersion51(SqliteConnection connexion, SqliteTransaction transaction)
    {
        Executer(connexion, transaction, """
            CREATE TABLE IF NOT EXISTS entite_actes (
                id              INTEGER PRIMARY KEY,
                le              INTEGER NOT NULL,
                auteur          INTEGER NOT NULL,
                sorte           TEXT    NOT NULL,
                genre           TEXT    NOT NULL,
                humain          INTEGER NOT NULL,
                communaute      INTEGER NOT NULL,
                priere          INTEGER NOT NULL,
                nombre          INTEGER NOT NULL,
                acte            TEXT    NOT NULL,
                resultats       TEXT    NOT NULL,
                issue           INTEGER NOT NULL,
                journal_premier INTEGER NOT NULL,
                journal_dernier INTEGER NOT NULL,
                echange         INTEGER NOT NULL,
                repris          INTEGER NOT NULL,
                defaire         TEXT    NOT NULL DEFAULT '',
                defait          INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS entite_actes_le ON entite_actes (le);
            CREATE INDEX IF NOT EXISTS entite_actes_humain ON entite_actes (humain);
            CREATE INDEX IF NOT EXISTS entite_actes_communaute ON entite_actes (communaute);
            """);
        // Une table journal existe toujours à ce stade ; recits_formes depuis la version 35 et entite_echanges depuis la 50.
        var plages = new List<(long Premier, long Dernier, long Echange)>();
        using (var c = connexion.CreateCommand())
        {
            c.Transaction = transaction;
            c.CommandText = "SELECT journal_premier, journal_dernier, id FROM entite_echanges WHERE journal_premier > 0 AND journal_dernier >= journal_premier;";
            using var l = c.ExecuteReader();
            while (l.Read()) plages.Add((l.GetInt64(0), l.GetInt64(1), l.GetInt64(2)));
        }
        var lignes = new List<LigneEntite>();
        using (var c = connexion.CreateCommand())
        {
            c.Transaction = transaction;
            c.CommandText = """
                SELECT j.id, j.secondes, j.type, j.humain, j.communaute, j.cause, coalesce(f.cle, '')
                FROM journal j LEFT JOIN recits_formes f ON f.id = j.forme WHERE j.origine = 1 ORDER BY j.id;
                """;
            using var l = c.ExecuteReader();
            while (l.Read()) lignes.Add(new LigneEntite(l.GetInt64(0), l.GetInt64(1), l.GetByte(2), l.GetInt32(3), l.GetInt32(4), l.GetInt64(5), l.GetString(6)));
        }
        using var ecrire = Preparer(connexion, transaction, $"INSERT INTO entite_actes ({ColonnesActes[4..]}) VALUES ($le, $auteur, $sorte, $genre, $h, $c, 0, $n, '', '[]', $issue, $jp, $jd, $e, 1, '', 0);",
            "$le", "$auteur", "$sorte", "$genre", "$h", "$c", "$n", "$issue", "$jp", "$jd", "$e");
        foreach (var a in Reconstituer(lignes))
        {
            var echange = plages.FirstOrDefault(p => a.Premier >= p.Premier && a.Premier <= p.Dernier).Echange;
            Lancer(ecrire, a.Le, echange > 0 ? 1 : 0, a.Sorte, a.Genre, a.Humain, a.Communaute, a.Nombre, 1, a.Premier, a.Dernier, echange);
        }
    }

    private sealed record LigneEntite(long Id, long Secondes, byte Type, int Humain, int Communaute, long Cause, string Cle);

    private sealed record ActeRepris(long Le, string Sorte, string Genre, int Humain, int Communaute, int Nombre, long Premier, long Dernier);

    /// <summary>
    /// Reconstitue les actes à partir des lignes « Entité » du journal : une ligne dont la cause n'est pas une ligne de l'Entité ouvre
    /// un acte ; les ouvertures de même sorte au même instant (une parole à plusieurs) n'en font qu'un ; les lignes qu'elles causent
    /// au même instant y sont rattachées.
    /// </summary>
    private static List<ActeRepris> Reconstituer(List<LigneEntite> lignes)
    {
        var ids = lignes.Select(l => l.Id).ToHashSet();
        var actes = new List<ActeRepris>();
        var humains = new HashSet<int>();
        ActeRepris? courant = null;
        foreach (var l in lignes)
        {
            var racine = l.Cause == 0 || !ids.Contains(l.Cause);
            if (!racine)
            {
                if (courant is not null && l.Secondes == courant.Le) courant = courant with { Dernier = l.Id };
                continue;
            }
            var (sorte, genre) = Classer(l);
            if (courant is not null && courant.Le == l.Secondes && courant.Sorte == sorte && courant.Genre == genre && sorte is "parler" or "penchant" or "exaucer" or "refuser")
            {
                if (l.Humain != 0) humains.Add(l.Humain);
                courant = courant with { Dernier = l.Id, Nombre = Math.Max(1, humains.Count), Humain = humains.Count > 1 ? 0 : courant.Humain };
                continue;
            }
            if (courant is not null) actes.Add(courant);
            humains.Clear();
            if (l.Humain != 0) humains.Add(l.Humain);
            courant = new ActeRepris(l.Secondes, sorte, genre, sorte is "reglage" ? 0 : l.Humain, l.Communaute, 1, l.Id, l.Id);
        }
        if (courant is not null) actes.Add(courant);
        return actes;
    }

    /// <summary>La sorte d'un acte d'après la ligne qui l'ouvre (clé du récit, sinon type d'événement).</summary>
    private static (string Sorte, string Genre) Classer(LigneEntite l)
    {
        var cle = l.Cle;
        string? geste = cle switch
        {
            "ges.mort" or "ges.mort.foudre" or "ges.mort.sommeil" or "ges.mort.soudain" => "FrapperDeMort",
            "ges.malade" => "RendreMalade",
            "ges.guerit" => "Guerir",
            "ges.fecond" => "RendreFecond",
            "ges.epidemie" => "Epidemie",
            "ges.calamite" => "Calamite",
            "ges.famine" => "Famine",
            "ges.finFamine" => "SortirDeFamine",
            "ges.arrete" => "ArreterEpidemie",
            "ges.fecondite" => "Fecondite",
            "ges.benit" => "Benir",
            _ => null,
        };
        if (geste is not null) return ("geste", geste);
        if (cle.StartsWith("j.esprits", StringComparison.Ordinal) || cle.StartsWith("j.clemence", StringComparison.Ordinal)) return ("reglage", "");
        if (cle is "pri.exaucee" or "pric.exaucee") return ("exaucer", "");
        if (cle.StartsWith("pri.refus", StringComparison.Ordinal) || cle.StartsWith("pric.refus", StringComparison.Ordinal)) return ("refuser", "");
        if (cle.StartsWith("fac.", StringComparison.Ordinal)) return ("souffler_combinaison", "");
        return l.Type switch
        {
            32 => ("geste", ""),                        // Geste
            19 => ("catastrophe", ""),                  // Catastrophe
            21 => ("penchant", ""),                     // Attirance
            31 => ("exaucer", ""),                      // Religion
            17 => ("reglage", ""),                      // Nature
            25 or 12 or 18 or 3 or 26 => ("parler", ""), // Parole, Humeur, Conflit, Union, Croyance
            _ => ("souffler_geste", ""),                // découvertes, savoirs, médecine… soufflés
        };
    }

    /// <summary>Enregistre un acte ; rend son numéro.</summary>
    public long AjouterActe(ActeEntite a)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = $"""
            INSERT INTO entite_actes ({ColonnesActes[4..]})
            VALUES ($le, $auteur, $sorte, $genre, $h, $c, $p, $n, $acte, $resultats, $issue, $jp, $jd, $e, $repris, $defaire, $defait) RETURNING id;
            """;
        commande.Parameters.AddWithValue("$le", a.Le);
        commande.Parameters.AddWithValue("$auteur", a.Auteur);
        commande.Parameters.AddWithValue("$sorte", a.Sorte);
        commande.Parameters.AddWithValue("$genre", a.Genre);
        commande.Parameters.AddWithValue("$h", a.Humain);
        commande.Parameters.AddWithValue("$c", a.Communaute);
        commande.Parameters.AddWithValue("$p", a.Priere);
        commande.Parameters.AddWithValue("$n", a.Nombre);
        commande.Parameters.AddWithValue("$acte", a.Acte);
        commande.Parameters.AddWithValue("$resultats", a.Resultats);
        commande.Parameters.AddWithValue("$issue", a.Issue);
        commande.Parameters.AddWithValue("$jp", a.JournalPremier);
        commande.Parameters.AddWithValue("$jd", a.JournalDernier);
        commande.Parameters.AddWithValue("$e", a.Echange);
        commande.Parameters.AddWithValue("$repris", a.Repris ? 1 : 0);
        commande.Parameters.AddWithValue("$defaire", a.Defaire);
        commande.Parameters.AddWithValue("$defait", a.Defait ? 1 : 0);
        return Convert.ToInt64(commande.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ConditionsActes(SqliteCommand commande, FiltreActes f)
    {
        var conditions = new List<string>();
        if (f.Auteur is { } a)
        {
            conditions.Add("auteur = $auteur");
            commande.Parameters.AddWithValue("$auteur", a);
        }
        if (!string.IsNullOrEmpty(f.Sorte))
        {
            // « geste.bienfait » et « geste.chatiment » : les gestes selon ce qu'ils font.
            if (f.Sorte is "geste.bienfait" or "geste.chatiment")
            {
                var bienfaits = string.Join(", ", GestesBienfaits.Select(g => $"'{g}'"));
                conditions.Add(f.Sorte == "geste.bienfait" ? $"sorte = 'geste' AND genre IN ({bienfaits})" : $"sorte = 'geste' AND genre NOT IN ({bienfaits})");
            }
            else
            {
                conditions.Add("sorte = $sorte");
                commande.Parameters.AddWithValue("$sorte", f.Sorte);
            }
        }
        if (f.Humain != 0)
        {
            conditions.Add("humain = $h");
            commande.Parameters.AddWithValue("$h", f.Humain);
        }
        if (f.Communaute != 0)
        {
            conditions.Add("communaute = $c");
            commande.Parameters.AddWithValue("$c", f.Communaute);
        }
        if (f.Depuis > 0)
        {
            conditions.Add("le >= $depuis");
            commande.Parameters.AddWithValue("$depuis", f.Depuis);
        }
        if (f.Jusqua > 0)
        {
            conditions.Add("le < $jusqua");
            commande.Parameters.AddWithValue("$jusqua", f.Jusqua);
        }
        return conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
    }

    /// <summary>Actes de l'Entité, du plus récent au plus ancien.</summary>
    public List<ActeEntite> Actes(FiltreActes f)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        var where = ConditionsActes(commande, f);
        if (f.Avant > 0)
        {
            where += (where.Length == 0 ? "WHERE " : " AND ") + "id < $avant";
            commande.Parameters.AddWithValue("$avant", f.Avant);
        }
        commande.CommandText = $"SELECT {ColonnesActes} FROM entite_actes {where} ORDER BY le DESC, id DESC LIMIT $limite;";
        commande.Parameters.AddWithValue("$limite", Math.Clamp(f.Limite, 1, 1000));
        using var l = commande.ExecuteReader();
        var liste = new List<ActeEntite>();
        while (l.Read()) liste.Add(LireActe(l));
        return liste;
    }

    /// <summary>Un acte par son numéro (null s'il n'existe pas).</summary>
    public ActeEntite? Acte(long id)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        commande.CommandText = $"SELECT {ColonnesActes} FROM entite_actes WHERE id = $id;";
        commande.Parameters.AddWithValue("$id", id);
        using var l = commande.ExecuteReader();
        return l.Read() ? LireActe(l) : null;
    }

    private static ActeEntite LireActe(SqliteDataReader l) => new(l.GetInt64(0), l.GetInt64(1), l.GetInt32(2), l.GetString(3), l.GetString(4), l.GetInt32(5),
        l.GetInt32(6), l.GetInt32(7), l.GetInt32(8), l.GetString(9), l.GetString(10), l.GetInt32(11), l.GetInt64(12), l.GetInt64(13), l.GetInt64(14),
        l.GetInt32(15) != 0, l.GetString(16), l.GetInt32(17) != 0);

    /// <summary>Comptes des actes (avec le même filtre, sans pagination) et nombre d'actes par année du monde.</summary>
    public ComptesActes CompterActes(FiltreActes f, long secondesParAn)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        var where = ConditionsActes(commande, f);
        var bienfaits = string.Join(", ", GestesBienfaits.Select(g => $"'{g}'"));
        commande.CommandText = $"""
            SELECT count(*), coalesce(sum(auteur = 1), 0),
                coalesce(sum(CASE WHEN sorte IN ('exaucer', 'exaucer_toutes') AND issue >= 0 THEN nombre ELSE 0 END), 0),
                coalesce(sum(CASE WHEN sorte IN ('refuser', 'refuser_toutes') AND issue >= 0 THEN nombre ELSE 0 END), 0),
                coalesce(sum(CASE WHEN sorte = 'geste' AND genre = 'FrapperDeMort' AND issue = 1 THEN 1 ELSE 0 END), 0),
                coalesce(sum(CASE WHEN sorte = 'geste' AND genre IN ({bienfaits}) AND issue = 1 THEN 1 ELSE 0 END), 0),
                coalesce(sum(CASE WHEN sorte = 'geste' AND genre NOT IN ({bienfaits}) AND issue = 1 THEN 1 ELSE 0 END), 0),
                coalesce(sum(sorte IN ('parler', 'parler_communaute', 'parler_monde', 'penchant')), 0),
                coalesce(sum(sorte IN ('souffler_geste', 'souffler_combinaison')), 0),
                coalesce(sum(sorte IN ('souffler_geste', 'souffler_combinaison') AND issue = 1), 0),
                coalesce(sum(sorte = 'catastrophe' AND issue >= 0), 0)
            FROM entite_actes {where};
            """;
        int[] v;
        using (var l = commande.ExecuteReader())
        {
            l.Read();
            v = Enumerable.Range(0, 11).Select(i => Convert.ToInt32(l.GetInt64(i))).ToArray();
        }
        commande.CommandText = $"SELECT le / $an, sum(auteur = 0), sum(auteur = 1) FROM entite_actes {where} GROUP BY le / $an ORDER BY 1;";
        commande.Parameters.AddWithValue("$an", secondesParAn);
        var parAn = new List<(int, int, int)>();
        using (var l = commande.ExecuteReader())
        {
            while (l.Read()) parAn.Add((l.GetInt32(0), l.GetInt32(1), l.GetInt32(2)));
        }
        return new ComptesActes(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], parAn);
    }

    /// <summary>Lignes du journal (table seulement) d'une plage de numéros, dans l'ordre ; <paramref name="entite"/> : seulement celles de l'Entité.</summary>
    public List<Simulation.EvenementJournal> LireJournalPlage(long premier, long dernier, bool entite, int limite = 60)
    {
        using var connexion = Ouvrir();
        using var commande = connexion.CreateCommand();
        using var transaction = connexion.BeginTransaction(deferred: true);
        commande.Transaction = transaction;
        commande.CommandText = $"SELECT {ColonnesJournal} FROM journal WHERE id BETWEEN $p AND $d {(entite ? "AND origine = 1" : "")} ORDER BY id LIMIT $limite;";
        commande.Parameters.AddWithValue("$p", premier);
        commande.Parameters.AddWithValue("$d", dernier);
        commande.Parameters.AddWithValue("$limite", Math.Clamp(limite, 1, 500));
        var lignes = new List<LigneJournal>();
        using (var l = commande.ExecuteReader())
        {
            while (l.Read()) lignes.Add(LireLigne(l));
        }
        var formes = FormesRecits.Lire(connexion, transaction);
        return [.. lignes.Select(x => VersEvenement(x, formes))];
    }
}
